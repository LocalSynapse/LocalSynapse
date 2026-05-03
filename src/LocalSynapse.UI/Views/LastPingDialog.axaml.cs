using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LocalSynapse.UI.Services.Localization;

namespace LocalSynapse.UI.Views;

/// <summary>마지막 전송 payload를 표시하는 읽기 전용 다이얼로그.</summary>
public partial class LastPingDialog : Window
{
    private string? _originalCopyText;

    /// <summary>LastPingDialog 생성자.</summary>
    public LastPingDialog(string? payload, string? sentAt)
    {
        InitializeComponent();

        // Localize window title (Tr markup extension doesn't work in Window.Title)
        var loc = App.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        Title = loc?[StringKeys.Security.Sends.LastSentTitle] ?? "Last Sent Payload";

        if (payload == null)
        {
            PayloadTextBox.Text = loc?[StringKeys.Security.Sends.LastSentNone]
                ?? "No data has been sent yet.";
            TimestampText.Text = "";
        }
        else
        {
            // Pretty-print the stored JSON
            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(payload);
                PayloadTextBox.Text = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                PayloadTextBox.Text = payload;
            }

            if (DateTime.TryParse(sentAt, out var dt))
            {
                var label = loc?[StringKeys.Security.Sends.LastSentTimestamp] ?? "Sent at";
                TimestampText.Text = $"{label}: {dt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            }
        }
    }

    /// <summary>Default parameterless constructor for Avalonia designer.</summary>
    public LastPingDialog() : this(null, null) { }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null || PayloadTextBox.Text == null) return;

        await clipboard.SetTextAsync(PayloadTextBox.Text);

        _originalCopyText ??= CopyButton.Content?.ToString();
        var loc = App.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        CopyButton.Content = loc?[StringKeys.Security.Sends.Copied] ?? "Copied!";

        _ = Task.Delay(1500).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                CopyButton.Content = _originalCopyText));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
