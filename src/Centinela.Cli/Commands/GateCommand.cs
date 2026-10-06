using System.Text.Json;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Cli;

/// <summary>
/// <c>centinela puerta [--umbrales f.json] [--salida f.json] [--solo verificador,guardian,auditor,impacto]</c>: pasa las
/// evaluaciones sobre las partes de DESARROLLO contra los umbrales de regresión y termina con código 1 si alguno falla. Es lo
/// que debe ejecutarse antes de aceptar un cambio de prompt, de modelo o de lógica de los agentes. Llama a modelos de pago
/// (unos céntimos), y por eso no forma parte de <c>dotnet test</c>; el consumo de cada evaluación se muestra al final.
/// </summary>
internal static class GateCommand
{
    private const string DefaultThresholds = "evaluaciones/umbrales.json";
    private static readonly string[] AllEvaluations = ["verificador", "guardian", "auditor", "impacto"];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var thresholdsPath = DefaultThresholds;
        string? outputPath = null;
        var only = AllEvaluations.ToList();

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            switch (args[i])
            {
                case "--umbrales": thresholdsPath = args[i + 1]; break;
                case "--salida": outputPath = args[i + 1]; break;
                case "--solo":
                    only = args[i + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (only.Except(AllEvaluations).Any()) return Fail($"Evaluación desconocida. Opciones: {string.Join(", ", AllEvaluations)}.");
                    break;
                default: return Fail($"Argumento no reconocido: {args[i]} {args[i + 1]}");
            }
        }

        if (!File.Exists(thresholdsPath)) return Fail($"No existe el fichero de umbrales: {thresholdsPath}");
        var thresholds = GateThresholds.Load(await File.ReadAllTextAsync(thresholdsPath, ct));

        Console.WriteLine($"Puerta de evaluación · parte «{thresholds.Split}» · {string.Join(", ", only)}\n");

        var checks = new List<GateCheck>();
        var usageByEvaluation = new Dictionary<string, IReadOnlyList<ModelUsage>>();

        foreach (var name in only)
        {
            Console.Write($"  midiendo {name}… ");
            using var scope = UsageScope.Begin();
            try
            {
                var result = name switch
                {
                    "verificador" => thresholds.Check(await MeasureVerifierAsync(services, thresholds.Split, ct)),
                    "guardian" => thresholds.Check(await MeasureGuardAsync(services, thresholds.Split, ct)),
                    "auditor" => thresholds.Check(await MeasureAuditorAsync(services, thresholds.Split, ct)),
                    _ => thresholds.Check(await ImpactCommands.MeasureAsync(
                        services, thresholds.SavedAnalysis, thresholds.SavedAnalysisRun, thresholds.Split, ct)),
                };
                checks.AddRange(result);
                Console.WriteLine(result.All(c => c.Passed) ? "ok" : "FALLA");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Una evaluación que no se puede medir es un fallo de la puerta, no un «no aplica».
                checks.Add(new GateCheck(name, "medición", $"error: {e.GetType().Name}: {e.Message}", "completarse", false));
                Console.WriteLine("ERROR");
            }

            usageByEvaluation[name] = scope.Snapshot();
        }

        var width = checks.Max(c => c.Metric.Length) + 2;
        Console.WriteLine();
        foreach (var group in checks.GroupBy(c => c.Evaluation))
        {
            Console.WriteLine(group.Key);
            foreach (var c in group)
            {
                Console.WriteLine($"  {(c.Passed ? "✔" : "✗")} {c.Metric.PadRight(width)} {c.Observed,-18} (exigido {c.Required})");
            }
        }

        var pricing = services.GetRequiredService<IOptions<PricingOptions>>().Value;
        var allUsage = usageByEvaluation.SelectMany(kv => kv.Value).ToList();
        var cost = CostEstimator.Estimate(allUsage, pricing);
        Console.WriteLine($"\nConsumo de la puerta: {allUsage.Sum(u => u.Calls):N0} llamadas · {allUsage.Sum(u => u.InputTokens):N0} tokens de entrada · " +
                          $"{allUsage.Sum(u => u.OutputTokens):N0} de salida · ≈ {cost.Usd:F2} USD estimados (precios de referencia)");

        var passed = checks.All(c => c.Passed);
        Console.WriteLine(passed ? "\nPUERTA SUPERADA" : $"\nPUERTA NO SUPERADA: {checks.Count(c => !c.Passed)} comprobación(es) fallida(s).");

        if (outputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                passed,
                split = thresholds.Split,
                checks,
                usage = allUsage,
                estimatedUsd = cost.Usd,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), ct);
            Console.WriteLine($"Resultado guardado en {outputPath}");
        }

        return passed ? 0 : 1;
    }

    private static async Task<EvalReport> MeasureVerifierAsync(IServiceProvider services, string split, CancellationToken ct)
    {
        var cases = VerifierEvaluation.LoadDataset(await File.ReadAllTextAsync("evaluaciones/verificador-citas.json", ct))
            .Where(c => c.Split == split).ToList();
        var outcomes = await VerifierEvaluation.RunAsync(services.GetRequiredService<ICitationVerifier>(), cases, parallelism: 4, ct);
        return EvalReport.From(outcomes);
    }

    private static async Task<GuardEvalReport> MeasureGuardAsync(IServiceProvider services, string split, CancellationToken ct)
    {
        var (attacks, hard) = GuardEvaluation.LoadDataset(await File.ReadAllTextAsync("evaluaciones/guardian-inyecciones.json", ct));
        var norm = await services.GetRequiredService<BoeClient>().GetDocumentAsync("BOE-A-2026-20587", ct);
        var sections = DocumentChunker.Chunk(norm).Where(c => c.Text.Length is >= 200 and <= 3500).ToList();
        var samples = GuardEvaluation.BuildSamples(sections, attacks, hard).Where(s => s.Split == split).ToList();

        var models = services.GetRequiredService<IOptions<ModelOptions>>().Value;
        var guard = new SecurityGuardAgent(services.GetRequiredService<ILanguageModel>(), Options.Create(models),
            Options.Create(new GuardOptions { Model = models.Fast }));

        var results = await GuardRunner.RunAsync(samples, guard, ct);
        return GuardEvaluation.Evaluate(results);
    }

    private static async Task<AuditEvalReport> MeasureAuditorAsync(IServiceProvider services, string split, CancellationToken ct)
    {
        const string Folder = "datos/empresa-ejemplo";
        var cases = AuditorEvaluation.LoadDataset(await File.ReadAllTextAsync("evaluaciones/auditor-borradores.json", ct))
            .Where(c => c.Finding.Split == split).ToList();

        var passages = Directory.GetFiles(Folder, "*.md").Order(StringComparer.Ordinal)
            .SelectMany(f => DocumentChunker.Chunk(CompanyDocumentLoader.Parse(
                File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), new Uri(Path.GetFullPath(f)), new DateOnly(2026, 1, 1))))
            .ToDictionary(c => (c.DocumentId, c.Label));
        var normDoc = await services.GetRequiredService<BoeClient>().GetDocumentAsync("BOE-A-2026-20587", ct);
        var normChunks = DocumentChunker.Chunk(normDoc).ToDictionary(c => c.Id);

        var auditor = new AuditorAgent(services.GetRequiredService<ILanguageModel>(), services.GetRequiredService<IOptions<ModelOptions>>());

        var results = await AuditorRunner.RunAsync(cases, passages, normChunks, normDoc.Title, auditor, withScope: false, ct);
        return AuditorEvaluation.Evaluate(results);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
