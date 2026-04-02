namespace LocalSynapse.Mcp.Interfaces;

public interface IMcpServer
{
    Task RunAsync(CancellationToken ct = default);
}
