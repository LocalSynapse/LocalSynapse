using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using LocalSynapse.Core.Interfaces;

namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Runtime localization service. Holds per-locale dictionaries for en/ko/fr/de/zh,
/// normalizes language codes, persists via ISettingsStore,
/// raises LanguageChanged on every SetLanguage call that actually changes state.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsStore _settings;
    private readonly Dictionary<string, Dictionary<string, string>> _registry;
    private string _current;

    private static readonly HashSet<string> SupportedLocales = ["en", "ko", "fr", "de", "zh"];

    /// <summary>Creates a new LocalizationService. Detects system locale on first run.</summary>
    public LocalizationService(ISettingsStore settings)
    {
        _settings = settings;
        _registry = LocalizationRegistry.Build();

        var raw = settings.GetLanguage();
        if (string.IsNullOrWhiteSpace(raw))
            raw = DetectSystemLanguage();
        _current = Normalize(raw);
        if (!string.Equals(raw, _current, StringComparison.Ordinal))
        {
            try { settings.SetLanguage(_current); }
            catch (Exception ex) { Debug.WriteLine($"[LocalizationService] Failed to persist language: {ex.Message}"); }
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

    /// <summary>Normalizes arbitrary locale codes to supported 2-letter codes.</summary>
    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        var prefix = code.Length >= 2 ? code[..2].ToLowerInvariant() : code.ToLowerInvariant();
        return SupportedLocales.Contains(prefix) ? prefix : "en";
    }

    /// <summary>Detects system UI language on first run.</summary>
    private static string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture switch
        {
            "ko" => "ko",
            "fr" => "fr",
            "de" => "de",
            "zh" => "zh",
            _ => "en"
        };
    }

    private string Resolve(string key)
    {
        if (_registry.TryGetValue(key, out var locales))
        {
            if (locales.TryGetValue(_current, out var value))
                return value;
            if (locales.TryGetValue("en", out var fallback))
                return fallback;
        }

#if DEBUG
        throw new KeyNotFoundException($"Localization key not found: '{key}'");
#else
        Debug.WriteLine($"[LocalizationService] Missing key: '{key}'");
        return key;
#endif
    }
}
