using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Mcp.Stdio;

var builder = Host.CreateApplicationBuilder(args);

// MCP 서버는 stdout을 JSON-RPC로 사용하므로, 로그는 stderr로 보낸다
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// LocalSynapse 서비스 등록 (Core + Search만, Pipeline/UI 제외)
McpServiceRegistration.AddLocalSynapseServices(builder.Services);

// MCP 서버 등록 — SDK가 stdio transport + 도구 스캔 처리
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(LocalSynapse.Mcp.Tools.LocalSynapseTools).Assembly);

var host = builder.Build();

// DB 마이그레이션
host.Services.GetRequiredService<IMigrationService>().RunMigrations();

await host.RunAsync();
