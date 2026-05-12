using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalSynapse.UI.Interfaces;

namespace LocalSynapse.UI.Services.Platform;

/// <summary>
/// macOS global hotkey registration via Carbon RegisterEventHotKey.
/// No Accessibility permission required for Carbon hotkeys.
/// </summary>
public sealed class MacOsHotkeyProvider : IGlobalHotkeyProvider
{
    private IntPtr _hotkeyRef;
    private IntPtr _handlerRef;
    private bool _registered;
    private GCHandle _delegateHandle;

    /// <inheritdoc />
    public event Action? HotkeyPressed;

    /// <inheritdoc />
    public bool TryRegister(string combo)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return false;

        Unregister();

        if (!ParseCombo(combo, out var modifiers, out var keyCode))
        {
            Debug.WriteLine($"[Hotkey/Mac] Failed to parse combo: {combo}");
            return false;
        }

        try
        {
            // Install event handler for kEventHotKeyPressed
            var eventSpec = new EventTypeSpec { eventClass = 0x6B657975 /* kEventClassKeyboard */, eventKind = 5 /* kEventHotKeyPressed */ };
            CarbonEventHandlerProc handler = OnCarbonHotKeyEvent;
            _delegateHandle = GCHandle.Alloc(handler);

            var status = InstallEventHandler(
                GetApplicationEventTarget(),
                handler,
                1,
                new[] { eventSpec },
                IntPtr.Zero,
                out _handlerRef);

            if (status != 0)
            {
                Debug.WriteLine($"[Hotkey/Mac] InstallEventHandler failed: {status}");
                return false;
            }

            var hotkeyId = new EventHotKeyID { signature = 0x4C53 /* 'LS' */, id = 1 };
            status = RegisterEventHotKey(keyCode, modifiers, hotkeyId, GetApplicationEventTarget(), 0, out _hotkeyRef);

            if (status != 0)
            {
                Debug.WriteLine($"[Hotkey/Mac] RegisterEventHotKey failed: {status}");
                RemoveEventHandler(_handlerRef);
                return false;
            }

            _registered = true;
            Debug.WriteLine($"[Hotkey/Mac] Registered: {combo}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Hotkey/Mac] Registration error: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (!_registered) return;
        try
        {
            if (_hotkeyRef != IntPtr.Zero)
                UnregisterEventHotKey(_hotkeyRef);
            if (_handlerRef != IntPtr.Zero)
                RemoveEventHandler(_handlerRef);
            if (_delegateHandle.IsAllocated)
                _delegateHandle.Free();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Hotkey/Mac] Unregister error: {ex.Message}");
        }
        _hotkeyRef = IntPtr.Zero;
        _handlerRef = IntPtr.Zero;
        _registered = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unregister();
    }

    private IntPtr OnCarbonHotKeyEvent(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        HotkeyPressed?.Invoke();
        return IntPtr.Zero; // noErr
    }

    private static bool ParseCombo(string combo, out uint modifiers, out uint keyCode)
    {
        modifiers = 0;
        keyCode = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CMD":
                case "COMMAND": modifiers |= 0x0100; break;   // cmdKey
                case "CTRL":
                case "CONTROL": modifiers |= 0x1000; break;   // controlKey
                case "SHIFT": modifiers |= 0x0200; break;     // shiftKey
                case "ALT":
                case "OPTION": modifiers |= 0x0800; break;    // optionKey
                case "SPACE": keyCode = 49; break;
                case "S": keyCode = 1; break;
                case "K": keyCode = 40; break;
                case "F": keyCode = 3; break;
                case "OEMTILDE":
                case "`": keyCode = 50; break;
                default:
                    Debug.WriteLine($"[Hotkey/Mac] Unknown key part: {part}");
                    return false;
            }
        }

        return keyCode != 0;
    }

    // Carbon P/Invoke declarations
    private delegate IntPtr CarbonEventHandlerProc(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(
        IntPtr target,
        CarbonEventHandlerProc handler,
        int numTypes,
        EventTypeSpec[] list,
        IntPtr userData,
        out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyID hotkeyId,
        IntPtr target,
        uint options,
        out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(IntPtr hotkeyRef);
}
