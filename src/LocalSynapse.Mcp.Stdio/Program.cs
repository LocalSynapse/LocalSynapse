using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Mcp.Interfaces;

namespace LocalSynapse.Mcp.Stdio;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.Error.WriteLine("localsynapse-mcp starting...");

        try
        {
            var services = new ServiceCollection();
            McpServiceRegistration.AddMcpServices(services);

            var provider = services.BuildServiceProvider();

            // DB 마이그레이션 (GUI가 먼저 실행되어 DB를 생성했을 수도 있고, 아닐 수도 있음)
            provider.GetRequiredService<IMigrationService>().RunMigrations();

            // MCP stdio 서버 시작 — stdin/stdout으로 JSON-RPC 수신 대기
            var server = provider.GetRequiredService<IMcpServer>();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await server.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — 정상 종료
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[localsynapse-mcp] Fatal error: {ex}");
            Console.Error.WriteLine($"localsynapse-mcp fatal error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
