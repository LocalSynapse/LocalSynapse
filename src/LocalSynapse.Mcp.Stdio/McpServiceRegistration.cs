using Microsoft.Extensions.DependencyInjection;
using LocalSynapse.Core.Database;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Repositories;
using LocalSynapse.Search.Adapters;
using LocalSynapse.Search.Interfaces;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Mcp.Stdio;

/// <summary>
/// MCP Stdio 서버에 필요한 최소 DI 서비스 집합.
/// Pipeline (스캔/인덱싱/임베딩)과 UI (Avalonia/ViewModel)는 등록하지 않는다.
/// MCP 도구 등록은 SDK의 WithToolsFromAssembly가 처리한다.
/// </summary>
internal static class McpServiceRegistration
{
    /// <summary>LocalSynapse Core + Search 서비스를 등록한다.</summary>
    public static void AddLocalSynapseServices(IServiceCollection services)
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
        // Placeholder for M2 dense search — NullEmbeddingBridge disables dense path
        services.AddSingleton<IEmbeddingBridge, NullEmbeddingBridge>();
        services.AddSingleton<IHybridSearch, HybridSearchService>();
    }
}
