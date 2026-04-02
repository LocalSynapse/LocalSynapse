using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            // MCP stdio server mode
            RunMcpServer(args).GetAwaiter().GetResult();
        }
        else
        {
            // GUI mode
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task RunMcpServer(string[] args)
    {
        Console.Error.WriteLine("LocalSynapse MCP Server starting...");

        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Services.DI.ServiceCollectionExtensions.AddLocalSynapseServices(sc);
        var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<Core.Interfaces.IMigrationService>().RunMigrations();

        var server = sp.GetRequiredService<Mcp.Interfaces.IMcpServer>();
        await server.RunAsync(CancellationToken.None);
    }
}
