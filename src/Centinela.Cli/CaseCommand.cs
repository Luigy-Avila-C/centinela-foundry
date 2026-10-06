using System.Text.Json;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centinela.Cli;

/// <summary>
/// <c>centinela caso &lt;resultado.json&gt;</c>: ejecuta el flujo completo del orquestador sobre una norma, con los
/// agentes reales de cribado, guardián, impacto, redactor y auditor. Un paso se sustituye y el comando lo dice:
/// el analista reutiliza un análisis ya guardado (para no pagar 93 verificaciones otra vez).
/// </summary>
internal static class CaseCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var resultPath = args[0];
        var run = 1;
        var normId = "BOE-A-2026-20587";
        string? reportArg = null;

        for (var i = 1; i < args.Length - 1; i += 2)
        {
            switch (args[i])
            {
                case "--ejecucion" when int.TryParse(args[i + 1], out var n) && n > 0: run = n; break;
                case "--norma": normId = args[i + 1]; break;
                case "--informe": reportArg = args[i + 1]; break;
                default: return Fail($"Argumento no reconocido: {args[i]} {args[i + 1]}");
            }
        }

        // «real» ejecuta el analista de verdad (varios minutos y muchas llamadas a modelos); un fichero reutiliza un análisis
        // guardado. Medir el coste de un caso completo exige lo primero.
        var realAnalyst = resultPath == "real";
        IRegulatoryAnalystAgent analyst;
        string analystNote;
        if (realAnalyst)
        {
            analyst = services.GetRequiredService<IRegulatoryAnalystAgent>();
            analystNote = "El analista se ejecuta DE VERDAD (análisis por artículo con verificación de citas).";
        }
        else
        {
            if (!File.Exists(resultPath)) return Fail($"No existe el resultado: {resultPath}");

            // El análisis guardado: las obligaciones con sus citas.
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, ct));
            var runs = doc.RootElement.GetProperty("Runs").EnumerateArray().ToList();
            if (run > runs.Count) return Fail($"El resultado tiene {runs.Count} ejecuciones.");

            var claims = runs[run - 1].GetProperty("Claims").EnumerateArray()
                .Select(c => new Claim(
                    c.GetProperty("Text").GetString()!,
                    c.GetProperty("Citations").EnumerateArray().Select(x => x.GetString()!).ToList()))
                .ToList();
            analyst = new ReplayAnalyst(new ChangeAnalysis("(análisis guardado)", [], claims.SelectMany(c => c.Citations).Distinct().ToList(), claims));
            analystNote = $"El analista reutiliza el análisis guardado de la ejecución {run} ({claims.Count} obligaciones): su coste NO está en la medida.";
        }

        // La norma con sus fragmentos: el redactor y el auditor los necesitan para comprobar las citas.
        var norm = await services.GetRequiredService<BoeClient>().GetDocumentAsync(normId, ct);
        var sections = DocumentChunker.Chunk(norm);
        var change = new RegulatoryChange(
            norm.Id, "BOE", norm.Title, norm.Url, norm.PublishedOn,
            string.Join("\n", norm.Paragraphs.Select(p => p.Text)), sections);

        Console.WriteLine("El guardián de seguridad (reglas + modelo) revisa el texto de la norma antes que nadie.");
        Console.WriteLine(analystNote + "\n");

        var repository = services.GetRequiredService<ICaseRepository>();
        var rounds = new List<(int Round, bool Passed, IReadOnlyList<string> Issues)>();
        var workflow = new ComplianceWorkflow(
            services.GetRequiredService<SecurityGuardAgent>(),
            services.GetRequiredService<ScreeningAgent>(),
            analyst,
            services.GetRequiredService<IImpactAgent>(),
            new LoggingDrafter(services.GetRequiredService<IDrafterAgent>()),
            new LoggingAuditor(services.GetRequiredService<IAuditorAgent>(), rounds),
            repository,
            services.GetRequiredService<ILogger<ComplianceWorkflow>>());

        var result = await workflow.RunAsync(change, ct);

        var reportPath = reportArg ?? Path.Combine("evaluaciones", "resultados", $"caso-{DateTime.Now:yyyyMMdd-HHmm}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, BuildReport(result, norm.Title, result.Analysis?.Claims?.Count ?? 0, rounds), ct);
        Console.WriteLine($"\nInforme guardado en: {reportPath}");

        var persistedIn = string.IsNullOrWhiteSpace(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Centinela.Infrastructure.Persistence.CosmosOptions>>().Value.Endpoint)
            ? "solo en memoria: se pierde al terminar (define Cosmos__Endpoint para guardarlo)"
            : "en Cosmos DB; apruébalo o recházalo desde la API";
        Console.WriteLine($"\nCaso {result.Id} guardado {persistedIn}.");
        Console.WriteLine("\nConsumo de modelos de este caso:\n" +
                          UsageReport.Format(result.Usage, services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PricingOptions>>().Value));

        Console.WriteLine($"\n══ RESULTADO: {result.Status} · {result.Revision} ronda(s) de redacción ══");
        foreach (var line in result.Log) Console.WriteLine($"  {line}");

        Console.WriteLine($"\nHallazgos: {result.Findings.Count} · borradores: {result.Actions.Count}");

        foreach (var action in result.Actions.OrderBy(a => a.PassageId, StringComparer.Ordinal))
        {
            Console.WriteLine($"\n──────── {action.DocumentId} · {action.PassageLabel}");
            Console.WriteLine($"ANTES:\n{Indent(action.OriginalText)}");
            Console.WriteLine($"DESPUÉS:\n{Indent(action.ProposedText)}");
            if (action.PendingData is { Count: > 0 })
            {
                Console.WriteLine($"PENDIENTE DE LA EMPRESA ({action.PendingData.Count}): {string.Join(" · ", action.PendingData)}");
            }

            Console.WriteLine($"NORMA: {string.Join(", ", action.NormCitations ?? [])}");
        }

        if (result.Status == CaseStatus.AwaitingHumanApproval)
        {
            Console.WriteLine("\nNADA SE HA APLICADO. El caso espera la aprobación de una persona; los datos marcados como");
            Console.WriteLine("«[COMPLETAR: …]» los tiene que aportar la empresa antes de aprobar.");
        }

        return result.Status == CaseStatus.Failed ? 2 : 0;
    }

    /// <summary>El informe de un caso en Markdown: qué se cambió, qué se discutió y qué debe completar la empresa.</summary>
    private static string BuildReport(
        ComplianceCase c, string normTitle, int obligations, IReadOnlyList<(int Round, bool Passed, IReadOnlyList<string> Issues)> rounds)
    {
        var sb = new System.Text.StringBuilder();
        string Quote(string? t) => string.Join("\n", (t ?? "").Split('\n').Select(l => "> " + l));

        sb.AppendLine("# Informe de caso");
        sb.AppendLine();
        sb.AppendLine($"**Norma:** {normTitle}");
        sb.AppendLine();
        sb.AppendLine("> **Datos ficticios.** La empresa («Distribuciones Aurora, S.L.») y sus documentos están inventados para probar Centinela.");
        sb.AppendLine("> **No se ha aplicado ningún cambio.** Cada borrador espera la aprobación de una persona, y los datos marcados como");
        sb.AppendLine("> `[COMPLETAR: …]` los tiene que aportar la empresa antes de aprobar.");
        sb.AppendLine();
        sb.AppendLine($"- Estado final: **{c.Status}** tras {c.Revision} ronda(s) de redacción");
        sb.AppendLine($"- Obligaciones analizadas: {obligations} · pasajes afectados: {c.Findings.Count} · borradores: {c.Actions.Count}");
        sb.AppendLine();
        sb.AppendLine("## Auditoría, ronda a ronda");
        sb.AppendLine();
        foreach (var (round, passed, issues) in rounds)
        {
            sb.AppendLine($"**Ronda {round}:** {(passed ? "superada" : $"{issues.Count} incidencia(s) bloqueante(s)")}");
            foreach (var issue in issues) sb.AppendLine($"- {issue}");
            sb.AppendLine();
        }

        sb.AppendLine("## Borradores");
        foreach (var a in c.Actions.OrderBy(a => a.PassageId, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine($"### {a.DocumentId} · {a.PassageLabel}");
            sb.AppendLine();
            sb.AppendLine("**Antes**");
            sb.AppendLine();
            sb.AppendLine(Quote(a.OriginalText));
            sb.AppendLine();
            sb.AppendLine("**Después (propuesto)**");
            sb.AppendLine();
            sb.AppendLine(Quote(a.ProposedText));
            sb.AppendLine();
            if (a.PendingData is { Count: > 0 }) sb.AppendLine($"**Pendiente de la empresa:** {string.Join(" · ", a.PendingData)}  ");
            sb.AppendLine($"**Norma:** {string.Join(", ", a.NormCitations ?? [])}  ");
            if (a.Justification.Length > 0) sb.AppendLine($"**Justificación del redactor:** {a.Justification}");
        }

        return sb.ToString();
    }

    private static string Indent(string? text) =>
        string.Join("\n", (text ?? "").Split('\n').Select(l => "    " + l));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    // ───── sustitutos explícitos para esta demostración ─────

    /// <summary>Devuelve un análisis ya guardado en lugar de regenerarlo.</summary>
    private sealed class ReplayAnalyst(ChangeAnalysis analysis) : IRegulatoryAnalystAgent
    {
        public Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange change, CancellationToken ct) => Task.FromResult(analysis);
    }


    private sealed class LoggingDrafter(IDrafterAgent inner) : IDrafterAgent
    {
        public async Task<IReadOnlyList<CorrectiveAction>> DraftAsync(
            ComplianceCase c, IReadOnlyList<string> previousIssues, CancellationToken ct)
        {
            Console.WriteLine($"  … redactando (ronda {c.Revision + 1}" +
                              $"{(previousIssues.Count > 0 ? $", corrigiendo {previousIssues.Count} incidencia(s)" : "")})");
            return await inner.DraftAsync(c, previousIssues, ct);
        }
    }

    private sealed class LoggingAuditor(
        IAuditorAgent inner, List<(int Round, bool Passed, IReadOnlyList<string> Issues)> history) : IAuditorAgent
    {
        public async Task<AuditVerdict> AuditAsync(ComplianceCase c, CancellationToken ct)
        {
            var verdict = await inner.AuditAsync(c, ct);
            history.Add((c.Revision, verdict.Passed, verdict.Issues));
            Console.WriteLine($"  … auditoría de la ronda {c.Revision}: {(verdict.Passed ? "SUPERADA" : $"{verdict.Issues.Count} incidencia(s)")}");
            foreach (var issue in verdict.Issues) Console.WriteLine($"        - {(issue.Length > 220 ? issue[..220] + "…" : issue)}");
            return verdict;
        }
    }
}
