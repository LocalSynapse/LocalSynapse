using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Runtime localization service. Holds En/Ko dictionary, normalizes language codes,
/// persists via ISettingsStore, raises LanguageChanged on every SetLanguage call that actually changes state.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsStore _settings;
    private readonly Dictionary<string, (string En, string Ko)> _registry;
    private string _current;

    /// <summary>Creates a new LocalizationService. Normalizes the stored language and persists if changed.</summary>
    public LocalizationService(ISettingsStore settings)
    {
        _settings = settings;
        _registry = LocalizationRegistry.Build();

        var raw = settings.GetLanguage() ?? "en";
        _current = Normalize(raw);
        if (!string.Equals(raw, _current, StringComparison.Ordinal))
        {
            try { settings.SetLanguage(_current); }
            catch (Exception ex) { Debug.WriteLine($"[LocalizationService] Failed to normalize stored language: {ex.Message}"); }
        }
    }

    /// <inheritdoc />
    public string Current => _current;

    /// <inheritdoc />
    public string this[string key] => Resolve(key);

    /// <inheritdoc />
    public string Format(string key, params object[] args)
    {
        var template = Resolve(key);
        try { return string.Format(CultureInfo.InvariantCulture, template, args); }
        catch (FormatException ex)
        {
            Debug.WriteLine($"[LocalizationService] Format failed for '{key}': {ex.Message}");
            return template;
        }
    }

    /// <inheritdoc />
    public void SetLanguage(string code)
    {
        var normalized = Normalize(code);
        if (string.Equals(_current, normalized, StringComparison.Ordinal)) return;

        _current = normalized;
        try { _settings.SetLanguage(normalized); }
        catch (Exception ex) { Debug.WriteLine($"[LocalizationService] Persist failed: {ex.Message}"); }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <summary>Normalizes arbitrary locale codes (e.g., "ko-KR", "en-US") to "ko" or "en".</summary>
    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        return code.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
    }

    private string Resolve(string key)
    {
        if (_registry.TryGetValue(key, out var pair))
            return _current == "ko" ? pair.Ko : pair.En;

#if DEBUG
        throw new KeyNotFoundException($"Localization key not found: '{key}'");
#else
        Debug.WriteLine($"[LocalizationService] Missing key: '{key}'");
        return key;
#endif
    }
}
