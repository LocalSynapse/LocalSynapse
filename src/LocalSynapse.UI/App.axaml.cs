using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.DI;
using LocalSynapse.UI.ViewModels;
using LocalSynapse.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _appLifetimeCts;

    /// <summary>Global service provider for the app.</summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>Single-instance guard, set by Program.Main before Avalonia starts.</summary>
    public static UI.Services.SingleInstanceGuard? SingleInstanceGuard { get; set; }

    /// <summary>When true, app starts hidden (tray-only). Set by --minimized flag.</summary>
    public static bool StartMinimized { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            SpeedDiagLog.AppStart(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");

            // 1. Build DI container
            var swDi = Stopwatch.StartNew();
            var services = new ServiceCollection();
            services.AddLocalSynapseServices();
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;
            SpeedDiagLog.Log("DI_BUILD", "time_ms", swDi.ElapsedMilliseconds);

            // 2. Run DB migrations
            var swMig = Stopwatch.StartNew();
            var migration = _serviceProvider.GetRequiredService<IMigrationService>();
            migration.RunMigrations();
            SpeedDiagLog.Log("MIGRATIONS", "time_ms", swMig.ElapsedMilliseconds);
            Debug.WriteLine("[App] Migrations complete");

            // 2.5. Sweep stale Updates/ artifacts (post-DI; see SPEC-IU-1 §4.3.1).
            // Wrapped: a sweep failure must NOT block app launch.
            try
            {
                var installer = _serviceProvider.GetRequiredService<UpdateInstallerService>();
                installer.SweepStaleArtifacts();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updates] Sweep failed: {ex.Message}");
            }

            // 3. Create main window with DI
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };

            // 3.5. Wire single-instance activation (second launch brings window to front)
            if (SingleInstanceGuard != null)
            {
                SingleInstanceGuard.ActivationRequested += () =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        desktop.MainWindow?.Show();
                        desktop.MainWindow?.Activate();
                    });
                };
            }

            // 3.6. Initialize global hotkey
            var hotkeyService = _serviceProvider.GetRequiredService<GlobalHotkeyService>();
            hotkeyService.Initialize(desktop.MainWindow!);

            // 3.7. Initialize tray icon
            var trayService = _serviceProvider.GetRequiredService<TrayIconService>();
            trayService.Initialize(desktop.MainWindow!, desktop);

            // 4. Start pipeline at APP LEVEL (survives all tab switches)
            _appLifetimeCts = new CancellationTokenSource();
            var orchestrator = _serviceProvider.GetRequiredService<IPipelineOrchestrator>();

            // Fire and forget — runs for the entire app lifetime
            _ = Task.Run(async () =>
            {
                try
                {
                    await orchestrator.StartAutoRunAsync(_appLifetimeCts.Token);
                }
                catch (OperationCanceledException) { /* app shutting down */ }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Pipeline fatal error: {ex.Message}");
                }
            });

            Debug.WriteLine("[App] Pipeline auto-run started at App level");

            // 4.5. Upgrade dialog / --minimized handling
            var appSettings = _serviceProvider.GetRequiredService<ISettingsStore>();
            var lastSeen = appSettings.GetLastSeenVersion();
            var stampRepo = _serviceProvider.GetRequiredService<Core.Interfaces.IPipelineStampRepository>();
            var stamps = stampRepo.GetCurrent();
            var isFirstRun = !stamps.ScanComplete && stamps.TotalFiles == 0;
            var needsUpgradeDialog = !isFirstRun
                && (lastSeen == null || VersionCompare(lastSeen, "2.13.0") < 0);

            if (needsUpgradeDialog)
            {
                // F7b: override --minimized — user MUST see upgrade dialog
                desktop.MainWindow!.Show();
                desktop.MainWindow!.Loaded += async (_, _) =>
                {
                    var autoStart = _serviceProvider!.GetRequiredService<AutoStartService>();
                    var vm = new AlwaysOnOnboardingViewModel(
                        appSettings, hotkeyService, autoStart, isUpgrade: true);
                    var dialog = new Views.AlwaysOnOnboardingDialog { DataContext = vm };
                    await dialog.ShowDialog(desktop.MainWindow);
                };
            }
            else if (StartMinimized)
            {
                // Tray-only start — window stays hidden
                Debug.WriteLine("[App] Starting minimized (tray-only)");
            }
            else
            {
                desktop.MainWindow!.Show();
            }

            // 5. Clean up on exit
            desktop.ShutdownRequested += (_, _) =>
            {
                Debug.WriteLine("[App] Shutdown requested");
                _appLifetimeCts?.Cancel();

                // Dispose ViewModels with timers before ServiceProvider
                if (_serviceProvider != null)
                {
                    _serviceProvider.GetService<DataSetupViewModel>()?.Dispose();
                    _serviceProvider.GetService<SearchViewModel>()?.Dispose();
                }

                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Safe version comparison handling partial versions like "2.13" vs "2.13.0".</summary>
    private static int VersionCompare(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);
        return -1; // parse failure → treat as older
    }
}
