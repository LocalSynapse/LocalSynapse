using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Pipeline.Chunking;
using LocalSynapse.Pipeline.Embedding;
using LocalSynapse.Pipeline.Interfaces;
using LocalSynapse.Pipeline.Orchestration;
using LocalSynapse.Pipeline.Parsing;
using LocalSynapse.Pipeline.Scanning;
using LocalSynapse.Search;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;
using LocalSynapse.Mcp.Interfaces;
using LocalSynapse.Mcp.Server;
using LocalSynapse.UI.ViewModels;
using LocalSynapse.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSynapse.UI.Services.DI;

/// <summary>
/// Full DI container configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Register all Agent services.</summary>
    public static IServiceCollection AddLocalSynapseServices(this IServiceCollection services)
    {
        // ── Core ──
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IMigrationService, MigrationService>();
        services.AddSingleton<IFileRepository, FileRepository>();
        services.AddSingleton<IChunkRepository, ChunkRepository>();
        services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
        services.AddSingleton<IPipelineStampRepository, PipelineStampRepository>();

        // ── Pipeline ──
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IContentExtractor, ContentExtractor>();
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<EmbeddingService>());
        services.AddSingleton<IModelInstaller, BgeM3Installer>();
        services.AddSingleton<IPipelineOrchestrator, PipelineOrchestrator>();

        // ── Search ──
        services.AddSingleton<IBm25Search, Bm25SearchService>();
        services.AddSingleton<IEmbeddingBridge>(sp =>
        {
            var embSvc = sp.GetRequiredService<EmbeddingService>();
            return new EmbeddingBridgeAdapter(embSvc);
        });
        services.AddSingleton<IDenseSearch, DenseSearchService>();
        services.AddSingleton<IHybridSearch, HybridSearchService>();
        services.AddSingleton<IDocumentFamilyService, DocumentFamilyService>();
        services.AddSingleton<ISnippetExtractor, SnippetExtractor>();
        services.AddSingleton<SearchClickService>();

        // ── MCP ──
        services.AddSingleton<McpToolRouter>(sp =>
        {
            var router = new McpToolRouter();
            var searchTool = new LocalSynapse.Mcp.Tools.SearchFilesTool(sp.GetRequiredService<IHybridSearch>());
            var fileTool = new LocalSynapse.Mcp.Tools.GetFileContentTool(sp.GetRequiredService<IFileRepository>());
            var statusTool = new LocalSynapse.Mcp.Tools.GetPipelineStatusTool(sp.GetRequiredService<IPipelineStampRepository>());
            var listTool = new LocalSynapse.Mcp.Tools.ListIndexedFilesTool(sp.GetRequiredService<IFileRepository>());
            router.Register("search_files", searchTool.ExecuteAsync);
            router.Register("get_file_content", fileTool.ExecuteAsync);
            router.Register("get_pipeline_status", statusTool.ExecuteAsync);
            router.Register("list_indexed_files", listTool.ExecuteAsync);
            return router;
        });
        services.AddSingleton<IMcpServer, McpServer>();

        // ── ViewModels ──
        // MainViewModel: Singleton (one navigation state for app lifetime)
        services.AddSingleton<MainViewModel>();
        // DataSetupViewModel: Singleton (observes pipeline, must survive tab switches)
        services.AddSingleton<DataSetupViewModel>();
        // Search: Singleton (survives tab switches, preserves search state)
        services.AddSingleton<SearchViewModel>(sp => new SearchViewModel(
            sp.GetRequiredService<IHybridSearch>(),
            sp.GetRequiredService<IBm25Search>(),
            sp.GetRequiredService<ISnippetExtractor>(),
            sp.GetRequiredService<IPipelineStampRepository>(),
            sp.GetRequiredService<IFileRepository>(),
            sp.GetRequiredService<IChunkRepository>()));
        services.AddSingleton<McpConfigService>();
        services.AddTransient<McpViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SecurityViewModel>();

        return services;
    }

    /// <summary>Pipeline.IEmbeddingService → Search.IEmbeddingBridge adapter.</summary>
    private sealed class EmbeddingBridgeAdapter : IEmbeddingBridge
    {
        private readonly EmbeddingService _inner;
        public EmbeddingBridgeAdapter(EmbeddingService inner) => _inner = inner;
        public bool IsReady => _inner.IsReady;
        public string? ActiveModelId => _inner.ActiveModelId;
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => _inner.GenerateEmbeddingAsync(text, ct);
    }
}
