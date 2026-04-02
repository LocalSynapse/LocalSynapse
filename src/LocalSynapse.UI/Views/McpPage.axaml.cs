using Avalonia.Controls;
using Avalonia.Interactivity;
using LocalSynapse.UI.ViewModels;

namespace LocalSynapse.UI.Views;

/// <summary>
/// MCP page code-behind. Handles clipboard operations.
/// </summary>
public partial class McpPage : UserControl
{
    public McpPage()
    {
        InitializeComponent();
    }

    /// <summary>Copy Claude Code add command to clipboard.</summary>
    private async void CopyAddCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is McpViewModel vm && TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(vm.ClaudeCodeAddCommand);
            if (sender is Button btn)
            {
                var original = btn.Content;
                btn.Content = "Copied!";
                await Task.Delay(1500);
                btn.Content = original;
            }
        }
    }

    /// <summary>Copy Claude Code remove command to clipboard.</summary>
    private async void CopyRemoveCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is McpViewModel vm && TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(vm.ClaudeCodeRemoveCommand);
            if (sender is Button btn)
            {
                var original = btn.Content;
                btn.Content = "Copied!";
                await Task.Delay(1500);
                btn.Content = original;
            }
        }
    }
}
