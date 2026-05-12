using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LocalSynapse.Core.Models;

namespace LocalSynapse.UI.Services;

/// <summary>
/// Prevents duplicate instances of the same runtime mode.
/// Windows: named Mutex + NamedPipe for activation signal.
/// macOS/Unix: exclusive file lock + Unix Domain Socket for activation signal.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly FileStream? _lockFileStream;
    private readonly bool _isOwner;
    private readonly string _ipcName;
    private readonly RuntimeMode _mode;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    /// <summary>True if this is the first (owning) instance.</summary>
    public bool IsFirstInstance => _isOwner;

    /// <summary>Fires on the UI thread when a second instance sends an activation signal.</summary>
    public event Action? ActivationRequested;

    /// <summary>Creates a single-instance guard for the given runtime mode.</summary>
    public SingleInstanceGuard(RuntimeMode mode)
    {
        _mode = mode;
        _ipcName = $"LocalSynapse_Activate_{mode}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mutexName = $"Local\\LocalSynapse_{mode}";
            _mutex = new Mutex(true, mutexName, out _isOwner);
        }
        else
        {
            // macOS/Unix: named Mutex does NOT work cross-process.
            // Use exclusive file lock instead.
            var lockPath = Path.Combine(Path.GetTempPath(), $"LocalSynapse_{mode}.lock");
            try
            {
                _lockFileStream = new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                _isOwner = true;
            }
            catch (IOException)
            {
                _isOwner = false;
            }
        }
    }

    /// <summary>Start listening for activation signals from second instances.</summary>
    public void StartListener()
    {
        if (!_isOwner) return;
        _listenerCts = new CancellationTokenSource();
        var ct = _listenerCts.Token;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _listenerTask = ListenNamedPipeAsync(ct);
        }
        else
        {
            _listenerTask = ListenUnixDomainSocketAsync(ct);
        }
    }

    /// <summary>Signal the first instance to activate its window, then return.</summary>
    public static void SignalExistingInstance(RuntimeMode mode)
    {
        var ipcName = $"LocalSynapse_Activate_{mode}";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var client = new NamedPipeClientStream(".", ipcName, PipeDirection.Out);
                client.Connect(timeout: 2000);
                client.WriteByte(1);
            }
            else
            {
                var socketPath = Path.Combine(Path.GetTempPath(), $"{ipcName}.sock");
                if (!File.Exists(socketPath)) return;
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(socketPath));
                socket.Send(new byte[] { 1 });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] Signal failed: {ex.Message}");
        }
    }

    private async Task ListenNamedPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_ipcName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Pipe error: {ex.Message}");
            }
        }
    }

    private async Task ListenUnixDomainSocketAsync(CancellationToken ct)
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"{_ipcName}.sock");
        // Clean up stale socket from previous crash
        if (File.Exists(socketPath)) File.Delete(socketPath);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var accepted = await listener.AcceptAsync(ct);
                ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Socket error: {ex.Message}");
            }
        }

        try { File.Delete(socketPath); } catch { /* cleanup best-effort */ }
    }

    /// <summary>Release all resources: mutex/file lock, IPC listener.</summary>
    public void Dispose()
    {
        _listenerCts?.Cancel();
        try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* timeout ok */ }
        _listenerCts?.Dispose();
        _lockFileStream?.Dispose();
        if (_isOwner && _mutex != null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { _mutex.ReleaseMutex(); } catch { /* already released */ }
        }
        _mutex?.Dispose();
    }
}
