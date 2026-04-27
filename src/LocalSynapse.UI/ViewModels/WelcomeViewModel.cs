using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.UI.ViewModels;

/// <summary>Scan scope option for first-run Welcome page.</summary>
public enum ScanScopeOption { AllDrives, MyDocuments, Custom }

/// <summary>
/// Welcome page ViewModel. First-run only: lets user choose scan scope
/// before pipeline starts.
/// </summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IPipelineOrchestrator _orchestrator;

    [ObservableProperty] private ScanScopeOption _selectedOption = ScanScopeOption.AllDrives;
    [ObservableProperty] private ObservableCollection<string> _customFolders = new();
    [ObservableProperty] private bool _canStart = true;

    /// <summary>Whether the custom folder list should be visible.</summary>
    public bool ShowFolderList => SelectedOption == ScanScopeOption.Custom;

    /// <summary>Whether All Drives card is selected.</summary>
    public bool IsAllDrivesSelected => SelectedOption == ScanScopeOption.AllDrives;

    /// <summary>Whether My Documents card is selected.</summary>
    public bool IsMyDocumentsSelected => SelectedOption == ScanScopeOption.MyDocuments;

    /// <summary>Whether Custom card is selected.</summary>
    public bool IsCustomSelected => SelectedOption == ScanScopeOption.Custom;

    /// <summary>Card border brush for All Drives.</summary>
    public IBrush CardBrushAllDrives => GetCardBrush(ScanScopeOption.AllDrives);
    /// <summary>Card border brush for My Documents.</summary>
    public IBrush CardBrushMyDocuments => GetCardBrush(ScanScopeOption.MyDocuments);
    /// <summary>Card border brush for Custom.</summary>
    public IBrush CardBrushCustom => GetCardBrush(ScanScopeOption.Custom);

    private IBrush GetCardBrush(ScanScopeOption opt)
    {
        Avalonia.Application.Current!.TryGetResource(
            SelectedOption == opt ? "AccentBrush" : "BorderDefaultBrush",
            Avalonia.Styling.ThemeVariant.Default, out var res);
        return (IBrush?)res ?? Brushes.Transparent;
    }

    /// <summary>WelcomeViewModel constructor.</summary>
    public WelcomeViewModel(ISettingsStore settingsStore, IPipelineOrchestrator orchestrator)
    {
        _settingsStore = settingsStore;
        _orchestrator = orchestrator;
    }

    partial void OnSelectedOptionChanged(ScanScopeOption value)
    {
        UpdateCanStart();
        OnPropertyChanged(nameof(ShowFolderList));
        OnPropertyChanged(nameof(IsAllDrivesSelected));
        OnPropertyChanged(nameof(IsMyDocumentsSelected));
        OnPropertyChanged(nameof(IsCustomSelected));
        OnPropertyChanged(nameof(CardBrushAllDrives));
        OnPropertyChanged(nameof(CardBrushMyDocuments));
        OnPropertyChanged(nameof(CardBrushCustom));
    }

    private void UpdateCanStart()
    {
        CanStart = SelectedOption != ScanScopeOption.Custom || CustomFolders.Count > 0;
    }

    /// <summary>Select a scan scope option (card click).</summary>
    [RelayCommand]
    private void SelectOption(ScanScopeOption opt) => SelectedOption = opt;

    /// <summary>Open folder picker dialog.</summary>
    [RelayCommand]
    private async Task AddFolderAsync(CancellationToken ct = default)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Select folders to index"
            });

        if (folders.Count == 0) return;

        foreach (var folder in folders)
        {
            var path = folder.Path.LocalPath;
            if (!CustomFolders.Contains(path))
                CustomFolders.Add(path);
        }
        UpdateCanStart();
    }

    /// <summary>Remove a folder from the custom list.</summary>
    [RelayCommand]
    private void RemoveFolder(string path)
    {
        CustomFolders.Remove(path);
        UpdateCanStart();
    }

    /// <summary>Save selection and start pipeline.</summary>
    [RelayCommand]
    private void StartIndexing()
    {
        switch (SelectedOption)
        {
            case ScanScopeOption.AllDrives:
                break;

            case ScanScopeOption.MyDocuments:
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _settingsStore.SetScanRoots(new[] { docs, desktop, downloads });
                break;

            case ScanScopeOption.Custom:
                _settingsStore.SetScanRoots(CustomFolders.ToArray());
                break;
        }

        _orchestrator.RequestImmediateCycle();
        WeakReferenceMessenger.Default.Send(new NavigateMessage(PageType.DataSetup));
    }
}
