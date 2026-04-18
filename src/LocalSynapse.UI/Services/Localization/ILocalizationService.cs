using System;

namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Runtime localization service with event-based language switching.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Current normalized language code ("en" or "ko").</summary>
    string Current { get; }

    /// <summary>Looks up localized text by key. Missing key: throws in Debug, returns key in Release.</summary>
    string this[string key] { get; }

    /// <summary>Formats a localized string with args (string.Format wrapper).</summary>
    string Format(string key, params object[] args);

    /// <summary>Changes the current language. Normalizes input, persists to settings, raises LanguageChanged.</summary>
    void SetLanguage(string code);

    /// <summary>Raised after the current language changes.</summary>
    event EventHandler? LanguageChanged;
}
