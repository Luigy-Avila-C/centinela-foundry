using Centinela.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Centinela.Evaluation;

public static class EvaluationServices
{
    /// <summary>
    /// Registra lo necesario para medir. El medidor de cobertura estricto exige una cita literal por cada elemento del hecho y
    /// da una cota INFERIOR de la cobertura (puede dar por no cubierto algo parafraseado); el laxo da una cota SUPERIOR
    /// (acepta afirmaciones «relacionadas»). Por defecto el estricto: en cumplimiento es peor dar por recogida una obligación
    /// que falta que revisar una de más. Configurable con <c>Coverage:Matcher=laxo</c>.
    /// </summary>
    public static IServiceCollection AddCentinelaEvaluation(this IServiceCollection services, IConfiguration configuration)
    {
        var lenient = string.Equals(configuration["Coverage:Matcher"], "laxo", StringComparison.OrdinalIgnoreCase);
        services.AddTransient<ICoverageMatcher>(sp => lenient
            ? new CoverageMatcherAgent(sp.GetRequiredService<ILanguageModel>())
            : new StrictCoverageMatcherAgent(sp.GetRequiredService<ILanguageModel>()));
        services.AddTransient<CoverageExperiment>();
        return services;
    }
}
