namespace LocalSynapse.UI.Interfaces;

/// <summary>Platform-specific global hotkey registration.</summary>
public interface IGlobalHotkeyProvider : IDisposable
{
    /// <summary>Attempt to register a hotkey combo. Returns true on success.</summary>
    bool TryRegister(string combo);

    /// <summary>Unregister the currently registered hotkey.</summary>
    void Unregister();

    /// <summary>Fires when the registered hotkey is pressed.</summary>
    event Action? HotkeyPressed;
}
