using Centinela.Application;
using Centinela.Infrastructure.Boe;
using Centinela.Infrastructure.Foundry;
using Centinela.Infrastructure.Persistence;
using Centinela.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Clave con la que se resuelve el índice de documentos de la empresa.</summary>
    public const string CompanyIndexKey = "empresa";

    /// <summary>Registra todo lo necesario para ejecutar los agentes contra Foundry y el BOE.</summary>
    public static IServiceCollection AddCentinela(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FoundryOptions>(configuration.GetSection(FoundryOptions.SectionName));
        services.Configure<ModelOptions>(configuration.GetSection(ModelOptions.SectionName));
        services.Configure<CompanyProfile>(configuration.GetSection(CompanyProfile.SectionName));

        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.SectionName));
        services.Configure<PricingOptions>(configuration.GetSection(PricingOptions.SectionName));

        services.AddSingleton<FoundryProject>();
        services.AddSingleton<ILanguageModel, FoundryLanguageModel>();
        services.AddSingleton<IEmbeddingModel, FoundryEmbeddingModel>();
        services.AddSingleton<IChunkIndex, AzureSearchChunkIndex>();

        // Guardián de seguridad: reglas del código + un modelo rápido, con decisión en el código.
        services.Configure<GuardOptions>(configuration.GetSection(GuardOptions.SectionName));
        services.AddTransient<SecurityGuardAgent>();
        services.AddTransient<ISecurityGuardAgent>(sp => sp.GetRequiredService<SecurityGuardAgent>());

        // Persistencia de los casos: Cosmos DB si hay endpoint; si no, memoria (se pierde al cerrar el proceso).
        services.Configure<CosmosOptions>(configuration.GetSection(CosmosOptions.SectionName));
        if (string.IsNullOrWhiteSpace(configuration[$"{CosmosOptions.SectionName}:Endpoint"]))
        {
            services.AddSingleton<ICaseRepository, InMemoryCaseRepository>();
        }
        else
        {
            services.AddSingleton<ICaseRepository, CosmosCaseRepository>();
        }

        services.AddTransient<ChunkIngestor>();
        services.AddTransient<ScreeningAgent>();
        services.AddTransient<IScreeningAgent>(sp => sp.GetRequiredService<ScreeningAgent>());
        // Segundo índice, solo con los documentos internos de la empresa. Es el que consulta el evaluador
        // de impacto; el de la normativa no se toca.
        services.Configure<ImpactOptions>(configuration.GetSection(ImpactOptions.SectionName));
        services.AddKeyedSingleton<IChunkIndex>(CompanyIndexKey, (sp, _) =>
        {
            var s = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
            return new AzureSearchChunkIndex(Options.Create(new SearchOptions
            {
                Endpoint = s.Endpoint,
                Dimensions = s.Dimensions,
                IndexName = s.CompanyIndexName,
            }));
        });
        services.AddTransient<ImpactAgent>(sp => new ImpactAgent(
            sp.GetRequiredService<ILanguageModel>(),
            sp.GetRequiredService<IEmbeddingModel>(),
            sp.GetRequiredKeyedService<IChunkIndex>(CompanyIndexKey),
            sp.GetRequiredService<IOptions<ModelOptions>>(),
            sp.GetRequiredService<IOptions<ImpactOptions>>()));
        services.AddTransient<IImpactAgent>(sp => sp.GetRequiredService<ImpactAgent>());

        services.Configure<AuditorOptions>(configuration.GetSection(AuditorOptions.SectionName));

        // Redactor y auditor. El auditor usa el modelo del juez, distinto del redactor a propósito.
        services.AddTransient<IDrafterAgent, DrafterAgent>();
        services.AddTransient<IAuditorAgent, AuditorAgent>();
        services.AddTransient<DrafterAgent>();
        services.AddTransient<AuditorAgent>();

        // Vigilante y el orquestador que usa. El Worker lo ejecuta solo si Watcher:Enabled=true.
        services.Configure<WatcherOptions>(configuration.GetSection(WatcherOptions.SectionName));
        services.AddTransient<ComplianceWorkflow>();
        services.AddTransient<IRegulatorySource, BoeRegulatorySource>();
        services.AddTransient<RegulatoryWatcher>();

        services.AddTransient<ICitationVerifier, CitationVerifierAgent>();
        services.AddTransient<RegulatoryAnalystAgent>();
        services.AddTransient<IRegulatoryAnalystAgent>(sp => sp.GetRequiredService<RegulatoryAnalystAgent>());

        services.AddHttpClient<BoeClient>(http =>
        {
            http.BaseAddress = new Uri("https://www.boe.es/");
            // El BOE pide identificarse; así saben quién consulta y pueden contactar si hay un problema.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("centinela-foundry/0.1");
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
