using System.Globalization;
using System.Text.Json;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Cli;

/// <summary>
/// Comandos del evaluador de impacto:
/// <list type="bullet">
/// <item><c>empresa-indexar &lt;carpeta&gt;</c>: indexa los documentos internos (Markdown) en el índice de la empresa.</item>
/// <item><c>impacto &lt;resultado.json&gt;</c>: qué pasajes de la empresa afecta una norma ya analizada.</item>
/// <item><c>evaluar-impacto &lt;resultado.json&gt;</c>: lo mide contra las etiquetas.</item>
/// </list>
/// Las obligaciones salen de un análisis guardado (<c>cobertura</c>), así no se paga volver a generarlo.
/// </summary>
internal static class ImpactCommands
{
    private const string DefaultFolder = "datos/empresa-ejemplo";
    private const string DefaultDataset = "evaluaciones/impacto-aurora.json";

    // ───────────────────────── empresa-indexar ─────────────────────────

    public static async Task<int> IndexCompanyAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var folder = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : DefaultFolder;
        if (!Directory.Exists(folder)) return Fail($"No existe la carpeta: {folder}");

        var ingestor = new ChunkIngestor(
            services.GetRequiredService<IEmbeddingModel>(),
            services.GetRequiredKeyedService<IChunkIndex>(DependencyInjection.CompanyIndexKey));

        var total = 0;
        foreach (var file in Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal))
        {
            var document = LoadDocument(file);
            var chunks = await ingestor.IngestAsync(document, ct);
            total += chunks.Count;
            Console.WriteLine($"{document.Id}: {chunks.Count} pasajes — {document.Title}");
        }

        Console.WriteLine($"\n{total} pasajes indexados en el índice de la empresa.");
        return 0;
    }

    // ───────────────────────── impacto ─────────────────────────

    public static async Task<int> AssessAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var options = Options.Parse(args);
        if (options.Error is not null) return Fail(options.Error);

        var analysis = LoadAnalysis(options.ResultPath, options.Run, out var error);
        if (analysis is null) return Fail(error!);

        var agent = BuildAgent(services, options.Deployment);
        Console.WriteLine($"Evaluando el impacto de {analysis.Claims!.Count} obligaciones sobre los documentos de la empresa" +
                          $"{(options.Deployment is null ? "" : $" (juez: {options.Deployment})")}…\n");

        var assessment = await agent.AssessDetailedAsync(analysis, ct);

        foreach (var f in assessment.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.PassageId, StringComparer.Ordinal))
        {
            Console.WriteLine($"[{f.Severity.ToString().ToUpperInvariant()}] {f.DocumentTitle} · {f.PassageLabel}");
            Console.WriteLine($"    en el documento: «{f.PassageQuote}»");
            Console.WriteLine($"    por qué: {Shorten(f.Rationale, 260)}");
            Console.WriteLine($"    qué cambiar: {Shorten(f.SuggestedAction ?? "", 260)}");
            Console.WriteLine($"    norma: {string.Join(", ", f.NormCitations ?? [])}\n");
        }

        Console.WriteLine($"{assessment.Findings.Count} pasajes afectados de {assessment.PassagesJudged} revisados" +
                          $" · {assessment.UnverifiedFindings} hallazgos descartados por cita no verificable");
        return 0;
    }

    // ───────────────────────── evaluar-impacto ─────────────────────────

    public static async Task<int> EvaluateAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var options = Options.Parse(args);
        if (options.Error is not null) return Fail(options.Error);

        var analysis = LoadAnalysis(options.ResultPath, options.Run, out var error);
        if (analysis is null) return Fail(error!);

        if (!File.Exists(options.Dataset)) return Fail($"No existe el conjunto de etiquetas: {options.Dataset}");

        var cases = ImpactEvaluation.LoadDataset(await File.ReadAllTextAsync(options.Dataset, ct))
            .Where(c => options.Split == "all" || c.Split == options.Split).ToList();
        if (cases.Count == 0) return Fail($"No hay casos en la parte '{options.Split}'.");

        var chunks = Directory.GetFiles(options.Folder, "*.md").Order(StringComparer.Ordinal)
            .SelectMany(f => DocumentChunker.Chunk(LoadDocument(f))).ToList();

        var agent = BuildAgent(services, options.Deployment);
        Console.WriteLine($"Evaluando impacto · parte «{options.Split}» ({cases.Count} pasajes etiquetados) · " +
                          $"{analysis.Claims!.Count} obligaciones · juez {options.Deployment ?? "(por defecto)"}\n");

        var assessment = await agent.AssessDetailedAsync(analysis, ct);
        var report = ImpactEvaluation.Evaluate(cases, chunks, assessment);

        string Pct(double d) => double.IsNaN(d) ? "n/d" : d.ToString("P0", CultureInfo.InvariantCulture);

        Console.WriteLine($"Sensibilidad (afectados encontrados) ... {Pct(report.Recall)}  ({report.TruePositives}/{report.Positives})");
        Console.WriteLine($"Precisión (marcados que lo están) ...... {Pct(report.Precision)}  ({report.TruePositives}/{report.TruePositives + report.FalsePositives})");
        Console.WriteLine($"Recuperación (afectados que llegaron al juez) {Pct(report.RetrievalRecall)}  ({report.PositivesRetrieved}/{report.Positives})");
        Console.WriteLine($"Gravedad: exacta {report.SeverityExact}/{report.SeverityComparable} · a ±1 nivel {report.SeverityWithinOne}/{report.SeverityComparable}");
        Console.WriteLine($"Dudosos (no cuentan): {report.Doubtful}, marcados {report.DoubtfulFlagged}");
        Console.WriteLine($"Hallazgos descartados: {assessment.UnverifiedFindings} por cita no verificable · {assessment.RejectedByRule} por regla " +
                          $"(no es un deber de la empresa, o se apoya en un silencio) · pasajes revisados: {assessment.PassagesJudged}");

        void List(string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            Console.WriteLine($"\n{title}");
            foreach (var i in items) Console.WriteLine($"  - {i}");
        }

        List("Falsos positivos (marcados y no afectados):", report.FalsePositiveCases);
        List("Falsos negativos (afectados y no marcados):", report.FalseNegativeCases);
        List("Afectados que ni se recuperaron (fallo de búsqueda):", report.NotRetrievedCases);

        // Hallazgos de este conjunto, para revisarlos a mano.
        var inSplit = cases.Select(c => chunks.First(x => x.DocumentId == c.DocumentId && x.Label == c.Section).Id).ToHashSet();
        Console.WriteLine("\nHallazgos en esta parte:");
        foreach (var f in assessment.Findings.Where(f => inSplit.Contains(f.PassageId!)).OrderBy(f => f.PassageId, StringComparer.Ordinal))
        {
            Console.WriteLine($"\n  [{f.Severity}] {f.DocumentId} · {f.PassageLabel}");
            Console.WriteLine($"      cita del documento: «{Shorten(f.PassageQuote ?? "", 160)}»");
            Console.WriteLine($"      motivo del juez: {Shorten(f.Rationale, 320)}");
            foreach (var o in (f.Obligations ?? []).Take(2)) Console.WriteLine($"      obligación: {Shorten(o, 200)}");
        }

        return 0;
    }

    /// <summary>Mide el evaluador de impacto sin imprimir nada (lo usa la puerta de evaluación).</summary>
    internal static async Task<ImpactEvalReport> MeasureAsync(
        IServiceProvider services, string resultPath, int run, string split, CancellationToken ct)
    {
        var analysis = LoadAnalysis(resultPath, run, out var error) ?? throw new InvalidOperationException(error);
        var cases = ImpactEvaluation.LoadDataset(await File.ReadAllTextAsync(DefaultDataset, ct))
            .Where(c => split == "all" || c.Split == split).ToList();
        var chunks = Directory.GetFiles(DefaultFolder, "*.md").Order(StringComparer.Ordinal)
            .SelectMany(f => DocumentChunker.Chunk(LoadDocument(f))).ToList();

        var assessment = await BuildAgent(services, null).AssessDetailedAsync(analysis, ct);
        return ImpactEvaluation.Evaluate(cases, chunks, assessment);
    }

    // ───────────────────────── auxiliares ─────────────────────────

    private static ImpactAgent BuildAgent(IServiceProvider services, string? deployment) => new(
        services.GetRequiredService<ILanguageModel>(),
        services.GetRequiredService<IEmbeddingModel>(),
        services.GetRequiredKeyedService<IChunkIndex>(DependencyInjection.CompanyIndexKey),
        services.GetRequiredService<IOptions<ModelOptions>>(),
        Microsoft.Extensions.Options.Options.Create(new ImpactOptions { Deployment = deployment }));

    private static SourceDocument LoadDocument(string file) => CompanyDocumentLoader.Parse(
        File.ReadAllText(file), Path.GetFileNameWithoutExtension(file), new Uri(Path.GetFullPath(file)), new DateOnly(2026, 1, 1));

    /// <summary>Lee las obligaciones de una ejecución guardada del análisis (el resultado de <c>cobertura</c>).</summary>
    private static ChangeAnalysis? LoadAnalysis(string path, int run, out string? error)
    {
        error = null;
        if (!File.Exists(path)) { error = $"No existe el resultado: {path}"; return null; }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var runs = doc.RootElement.GetProperty("Runs").EnumerateArray().ToList();
        if (run < 1 || run > runs.Count) { error = $"El resultado tiene {runs.Count} ejecuciones; se pidió la {run}."; return null; }

        var claims = runs[run - 1].GetProperty("Claims").EnumerateArray()
            .Select(c => new Claim(
                c.GetProperty("Text").GetString()!,
                c.GetProperty("Citations").EnumerateArray().Select(x => x.GetString()!).ToList()))
            .ToList();

        return new ChangeAnalysis("(análisis guardado)", [], claims.SelectMany(c => c.Citations).Distinct().ToList(), claims);
    }

    private static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed record Options(
        string ResultPath, int Run, string Split, string Folder, string Dataset, string? Deployment, string? Error)
    {
        public static Options Parse(string[] args)
        {
            if (args.Length == 0) return new("", 1, "dev", DefaultFolder, DefaultDataset, null, "Falta el resultado guardado (p. ej. evaluaciones/resultados/cobertura-….json).");

            string split = "dev", folder = DefaultFolder, dataset = DefaultDataset;
            string? deployment = null;
            var run = 1;

            for (var i = 1; i < args.Length - 1; i += 2)
            {
                var v = args[i + 1];
                switch (args[i])
                {
                    case "--ejecucion" when int.TryParse(v, out var n) && n > 0: run = n; break;
                    case "--parte" when v is "dev" or "test" or "all": split = v; break;
                    case "--carpeta": folder = v; break;
                    case "--etiquetas": dataset = v; break;
                    case "--modelo": deployment = v; break;
                    default: return new(args[0], run, split, folder, dataset, deployment, $"Argumento no reconocido: {args[i]} {v}");
                }
            }

            return new(args[0], run, split, folder, dataset, deployment, null);
        }
    }
}
