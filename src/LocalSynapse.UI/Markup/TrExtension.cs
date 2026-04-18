using System;
using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using LocalSynapse.UI.Services.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI.Markup;

/// <summary>
/// XAML markup extension: {i18n:Tr Key=X} → localized string that auto-refreshes on language change.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    /// <summary>Localization key registered in StringKeys / LocalizationRegistry.</summary>
    public string? Key { get; set; }

    /// <summary>Default constructor (Key set via property initializer).</summary>
    public TrExtension() { }

    /// <summary>Positional key constructor — allows {i18n:Tr Search.Header} shorthand.</summary>
    public TrExtension(string key) { Key = key; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var key = Key ?? string.Empty;
        var source = new TrBindingSource(key);
        return new Binding
        {
            Source = source,
            Path = nameof(TrBindingSource.Value),
            Mode = BindingMode.OneWay,
        };
    }
}

/// <summary>
/// Observable wrapper around ILocalizationService for a single key. Raises PropertyChanged("Value")
/// whenever the service's LanguageChanged event fires.
/// </summary>
internal sealed class TrBindingSource : INotifyPropertyChanged
{
    private readonly string _key;
    private readonly ILocalizationService? _loc;

    public TrBindingSource(string key)
    {
        _key = key;
        _loc = App.Services?.GetService<ILocalizationService>();
        if (_loc != null)
            _loc.LanguageChanged += OnLanguageChanged;
    }

    public string Value => _loc != null ? _loc[_key] : _key;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
