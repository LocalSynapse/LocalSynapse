using Microsoft.Extensions.DependencyInjection;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Mcp.Interfaces;
using LocalSynapse.Mcp.Server;
using LocalSynapse.Search.Adapters;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Mcp.Stdio;

/// <summary>
/// MCP Stdio 서버에 필요한 최소 DI 서비스 집합.
/// Pipeline (스캔/인덱싱/임베딩)과 UI (Avalonia/ViewModel)는 등록하지 않는다.
/// </summary>
internal static class McpServiceRegistration
{
    /// <summary>MCP 전용 서비스를 등록한다.</summary>
    public static void AddMcpServices(IServiceCollection services)
    {
        // ── Core ──
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IMigrationService, MigrationService>();

        // ── Repositories ──
        services.AddSingleton<IFileRepository, FileRepository>();
        services.AddSingleton<IChunkRepository, ChunkRepository>();
        services.AddSingleton<IPipelineStampRepository, PipelineStampRepository>();
        services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();

        // ── Search ──
        services.AddSingleton<SearchClickService>();
        services.AddSingleton<Bm25SearchService>(sp => new Bm25SearchService(
            sp.GetRequiredService<SqliteConnectionFactory>(),
            sp.GetRequiredService<SearchClickService>()));
        services.AddSingleton<IBm25Search>(sp => sp.GetRequiredService<Bm25SearchService>());
        services.AddSingleton<IDenseSearch, EmptyDenseSearch>();
        services.AddSingleton<IEmbeddingBridge, NullEmbeddingBridge>();
        services.AddSingleton<IDocumentFamilyService, DocumentFamilyService>();
        services.AddSingleton<ISnippetExtractor, SnippetExtractor>();
        services.AddSingleton<IHybridSearch, HybridSearchService>();

        // ── MCP ──
        services.AddSingleton<McpToolRouter>(sp =>
        {
            var router = new McpToolRouter();
            var searchTool = new LocalSynapse.Mcp.Tools.SearchFilesTool(
                sp.GetRequiredService<IHybridSearch>());
            var fileTool = new LocalSynapse.Mcp.Tools.GetFileContentTool(
                sp.GetRequiredService<IFileRepository>(),
                sp.GetRequiredService<IChunkRepository>());
            var statusTool = new LocalSynapse.Mcp.Tools.GetPipelineStatusTool(
                sp.GetRequiredService<IPipelineStampRepository>());
            var listTool = new LocalSynapse.Mcp.Tools.ListIndexedFilesTool(
                sp.GetRequiredService<IFileRepository>());
            router.Register("search_files", searchTool.ExecuteAsync);
            router.Register("get_file_content", fileTool.ExecuteAsync);
            router.Register("get_pipeline_status", statusTool.ExecuteAsync);
            router.Register("list_indexed_files", listTool.ExecuteAsync);
            return router;
        });
        services.AddSingleton<IMcpServer, McpServer>();
    }
}
