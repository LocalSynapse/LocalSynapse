using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalSynapse.UI.Interfaces;

namespace LocalSynapse.UI.Services.Platform;

/// <summary>
/// Windows global hotkey registration via Win32 RegisterHotKey.
/// Uses a background thread with a message loop to receive WM_HOTKEY.
/// </summary>
public sealed class WindowsHotkeyProvider : IGlobalHotkeyProvider
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4C53; // "LS" in hex
    private Thread? _messageThread;
    private volatile bool _registered;
    private volatile bool _disposed;
    private IntPtr _hwnd;

    /// <inheritdoc />
    public event Action? HotkeyPressed;

    /// <inheritdoc />
    public bool TryRegister(string combo)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        Unregister();

        if (!ParseCombo(combo, out var modifiers, out var vk))
        {
            Debug.WriteLine($"[Hotkey/Win] Failed to parse combo: {combo}");
            return false;
        }

        // RegisterHotKey must be called from the thread that owns the message loop
        var success = false;
        var ready = new ManualResetEventSlim();

        _messageThread = new Thread(() =>
        {
            // Create a message-only window
            _hwnd = IntPtr.Zero; // Using thread-based message loop (hwnd = IntPtr.Zero)
            success = NativeMethods.RegisterHotKey(IntPtr.Zero, HOTKEY_ID, modifiers, vk);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[Hotkey/Win] RegisterHotKey failed: error {error}");
            }
            _registered = success;
            ready.Set();

            if (!success) return;

            // Message pump
            while (!_disposed && NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY && msg.wParam == (IntPtr)HOTKEY_ID)
                {
                    HotkeyPressed?.Invoke();
                }
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        })
        {
            IsBackground = true,
            Name = "GlobalHotkey",
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));

        return success;
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (_registered && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Post WM_QUIT to stop the message loop
            if (_messageThread?.IsAlive == true)
            {
                NativeMethods.PostThreadMessage(
                    (uint)_messageThread.ManagedThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }

            NativeMethods.UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
            _registered = false;
            _messageThread?.Join(TimeSpan.FromSeconds(2));
            _messageThread = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Unregister();
    }

    private static bool ParseCombo(string combo, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "ALT": modifiers |= 0x0001; break;       // MOD_ALT
                case "CTRL":
                case "CONTROL": modifiers |= 0x0002; break;   // MOD_CONTROL
                case "SHIFT": modifiers |= 0x0004; break;     // MOD_SHIFT
                case "WIN":
                case "SUPER": modifiers |= 0x0008; break;     // MOD_WIN
                case "SPACE": vk = 0x20; break;
                case "S": vk = 0x53; break;
                case "K": vk = 0x4B; break;
                case "F": vk = 0x46; break;
                case "OEMTILDE":
                case "`": vk = 0xC0; break;
                default:
                    Debug.WriteLine($"[Hotkey/Win] Unknown key part: {part}");
                    return false;
            }
        }

        // MOD_NOREPEAT (Windows 7+)
        modifiers |= 0x4000;

        return vk != 0;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }
    }
}
