using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LocalSynapse.UI.Services.Localization;
using LocalSynapse.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI.Views;

public partial class AlwaysOnOnboardingDialog : Window
{
    public AlwaysOnOnboardingDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not AlwaysOnOnboardingViewModel vm) return;

        var loc = App.Services?.GetService<ILocalizationService>();
        if (loc == null) return;

        // Set localized strings based on variant
        var titleBlock = this.FindControl<TextBlock>("TitleBlock");
        var bodyBlock = this.FindControl<TextBlock>("BodyBlock");
        var autoStartCheck = this.FindControl<CheckBox>("AutoStartCheck");
        var confirmButton = this.FindControl<Button>("ConfirmButton");

        if (vm.IsUpgrade)
        {
            if (titleBlock != null) titleBlock.Text = loc[StringKeys.AlwaysOn.OnboardUpgradeTitle];
            if (bodyBlock != null) bodyBlock.Text = loc[StringKeys.AlwaysOn.OnboardUpgradeBody];
            if (autoStartCheck != null) autoStartCheck.Content = loc[StringKeys.AlwaysOn.OnboardUpgradeAutoStart];
            if (confirmButton != null) confirmButton.Content = loc[StringKeys.AlwaysOn.OnboardUpgradeConfirm];
        }
        else
        {
            if (titleBlock != null) titleBlock.Text = loc[StringKeys.AlwaysOn.OnboardFreshTitle];
            if (bodyBlock != null) bodyBlock.Text = loc[StringKeys.AlwaysOn.OnboardFreshBody];
            if (autoStartCheck != null) autoStartCheck.Content = loc[StringKeys.AlwaysOn.OnboardFreshAutoStart];
            if (confirmButton != null) confirmButton.Content = loc[StringKeys.AlwaysOn.OnboardFreshConfirm];
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // If confirmed via command, allow close; otherwise treat X as confirm
        if (DataContext is AlwaysOnOnboardingViewModel vm && !vm.IsConfirmed)
        {
            vm.ConfirmCommand.Execute(null);
        }
        base.OnClosing(e);
    }
}
