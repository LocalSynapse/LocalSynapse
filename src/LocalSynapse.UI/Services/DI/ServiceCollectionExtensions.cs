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
using LocalSynapse.UI.ViewModels;
using LocalSynapse.UI.Services;
using LocalSynapse.UI.Services.Localization;
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
        services.AddSingleton<IFileScanner>(sp => new FileScanner(
            sp.GetRequiredService<IFileRepository>(),
            sp.GetRequiredService<IPipelineStampRepository>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<IContentExtractor, ContentExtractor>();
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<EmbeddingService>());
        services.AddSingleton<IModelInstaller, BgeM3Installer>();
        services.AddSingleton<GpuDetectionService>();
        services.AddSingleton<IPipelineOrchestrator>(sp => new PipelineOrchestrator(
            sp.GetRequiredService<IFileScanner>(),
            sp.GetRequiredService<IContentExtractor>(),
            sp.GetRequiredService<ITextChunker>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<IModelInstaller>(),
            sp.GetRequiredService<IFileRepository>(),
            sp.GetRequiredService<IChunkRepository>(),
            sp.GetRequiredService<IEmbeddingRepository>(),
            sp.GetRequiredService<IPipelineStampRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<GpuDetectionService>()));

        // ── Search ──
        services.AddSingleton<Bm25SearchService>(sp => new Bm25SearchService(
            sp.GetRequiredService<SqliteConnectionFactory>(),
            sp.GetRequiredService<SearchClickService>()));
        services.AddSingleton<IBm25Search>(sp => sp.GetRequiredService<Bm25SearchService>());
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

        // ── Localization ──
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // ── Telemetry Counters ──
        services.AddSingleton<TelemetryCounterService>();

        // ── Update Check ──
        services.AddSingleton<UpdateCheckService>();

        // ── ViewModels ──
        // MainViewModel: Singleton (one navigation state for app lifetime)
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp, sp.GetRequiredService<UpdateCheckService>(),
            sp.GetRequiredService<IPipelineStampRepository>()));
        // DataSetupViewModel: Singleton (observes pipeline, must survive tab switches)
        services.AddSingleton<DataSetupViewModel>();
        // Search: Singleton (survives tab switches, preserves search state)
        services.AddSingleton<SearchViewModel>(sp => new SearchViewModel(
            sp.GetRequiredService<IHybridSearch>(),
            sp.GetRequiredService<IBm25Search>(),
            sp.GetRequiredService<Bm25SearchService>(),
            sp.GetRequiredService<ISnippetExtractor>(),
            sp.GetRequiredService<IPipelineStampRepository>(),
            sp.GetRequiredService<IFileRepository>(),
            sp.GetRequiredService<IChunkRepository>(),
            sp.GetRequiredService<SearchClickService>(),
            sp.GetRequiredService<IDocumentFamilyService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<IModelInstaller>(),
            sp.GetRequiredService<TelemetryCounterService>()));
        services.AddSingleton<McpConfigService>();
        services.AddTransient<McpViewModel>();
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<UpdateCheckService>()));
        services.AddSingleton<SecurityViewModel>(sp => new SecurityViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<UpdateCheckService>()));
        services.AddTransient<WelcomeViewModel>(sp => new WelcomeViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IPipelineOrchestrator>()));

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
