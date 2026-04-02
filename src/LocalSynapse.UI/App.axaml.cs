using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Pipeline.Interfaces;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 1. Build DI container
            var services = new ServiceCollection();
            services.AddLocalSynapseServices();
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;

            // 2. Run DB migrations
            var migration = _serviceProvider.GetRequiredService<IMigrationService>();
            migration.RunMigrations();
            Debug.WriteLine("[App] Migrations complete");

            // 3. Create main window with DI
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };

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

            // 5. Clean up on exit
            desktop.ShutdownRequested += (_, _) =>
            {
                Debug.WriteLine("[App] Shutdown requested");
                _appLifetimeCts?.Cancel();
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
