using System.Globalization;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Cli;

/// <summary>
/// Comandos del redactor y el auditor. <c>evaluar-auditor</c> mide si el auditor aprueba los borradores buenos y rechaza los
/// defectuosos del conjunto etiquetado (<c>evaluaciones/auditor-borradores.json</c>).
/// </summary>
internal static class DraftingCommands
{
    private const string DefaultFolder = "datos/empresa-ejemplo";
    private const string DefaultDataset = "evaluaciones/auditor-borradores.json";

    public static async Task<int> EvaluateAuditorAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var split = "dev";
        string? judge = null;
        var folder = DefaultFolder;
        var dataset = DefaultDataset;
        var verbose = true;
        var withScope = false;
        var votes = 1;
        var repetitions = 1;

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var v = args[i + 1];
            switch (args[i])
            {
                case "--con-alcance" when v is "si" or "no": withScope = v == "si"; break;
                case "--votos" when int.TryParse(v, out var n) && n > 0 && n % 2 == 1: votes = n; break;
                case "--repeticiones" when int.TryParse(v, out var r) && r > 0: repetitions = r; break;
                case "--parte" when v is "dev" or "test" or "all": split = v; break;
                case "--modelo": judge = v; break;
                case "--carpeta": folder = v; break;
                case "--etiquetas": dataset = v; break;
                case "--detalle" when v is "si" or "no": verbose = v == "si"; break;
                default: return Fail($"Argumento no reconocido: {args[i]} {v}");
            }
        }

        if (!File.Exists(dataset)) return Fail($"No existe el conjunto de borradores: {dataset}");

        var cases = AuditorEvaluation.LoadDataset(await File.ReadAllTextAsync(dataset, ct))
            .Where(c => split == "all" || c.Finding.Split == split).ToList();
        if (cases.Count == 0) return Fail($"No hay borradores en la parte '{split}'.");

        // Pasajes originales de la empresa y fragmentos de la norma (se descarga el BOE: es público y no cuesta nada).
        var passages = Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal)
            .SelectMany(f => DocumentChunker.Chunk(CompanyDocumentLoader.Parse(
                File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), new Uri(Path.GetFullPath(f)), new DateOnly(2026, 1, 1))))
            .ToDictionary(c => (c.DocumentId, c.Label));

        var boe = services.GetRequiredService<BoeClient>();
        var normDoc = await boe.GetDocumentAsync("BOE-A-2026-20587", ct);
        var normChunks = DocumentChunker.Chunk(normDoc).ToDictionary(c => c.Id);

        var models = services.GetRequiredService<IOptions<ModelOptions>>().Value;
        var auditor = new AuditorAgent(
            services.GetRequiredService<ILanguageModel>(),
            Options.Create(new ModelOptions { Smart = models.Smart, Judge = judge ?? models.Judge }),
            Options.Create(new AuditorOptions { Votes = votes }));

        Console.WriteLine($"Evaluando el auditor · parte «{split}» · {cases.Count} borradores · juez {judge ?? models.Judge}" +
                          $" · {votes} voto(s) por auditoría · {(withScope ? "con" : "sin")} alcance" +
                          $"{(repetitions > 1 ? $" · {repetitions} repeticiones de la MISMA entrada" : "")}\n");

        Task<(AuditDraftCase Case, AuditResult Result)[]> RunOnceAsync() =>
            AuditorRunner.RunAsync(cases, passages, normChunks, normDoc.Title, auditor, withScope, ct);

        string Pct(double d) => double.IsNaN(d) ? "n/d" : d.ToString("P0", CultureInfo.InvariantCulture);

        // Consistencia: mismo borrador, misma entrada, varias auditorías. ¿Cambia el veredicto?
        if (repetitions > 1)
        {
            var runs = new List<(AuditDraftCase Case, AuditResult Result)[]>();
            for (var r = 1; r <= repetitions; r++)
            {
                runs.Add(await RunOnceAsync());
                Console.WriteLine($"  … repetición {r}/{repetitions} hecha");
            }

            var reports = runs.Select(r => AuditorEvaluation.Evaluate(r)).ToList();

            Console.WriteLine("\nPor repetición (defectuosos rechazados · buenos aprobados):");
            for (var r = 0; r < reports.Count; r++)
            {
                Console.WriteLine($"  #{r + 1}: {reports[r].FlawedRejected}/{reports[r].FlawedTotal} · {reports[r].GoodApproved}/{reports[r].GoodTotal}");
            }

            var unstable = new List<string>();
            foreach (var c in cases)
            {
                var passes = runs.Count(run => run.First(x => x.Case.Id == c.Id).Result.Passed);
                if (passes != 0 && passes != repetitions)
                {
                    unstable.Add($"{c.Id} [{(c.ShouldApprove ? "bueno" : $"defectuoso/{c.Defect}")}]: aprobado {passes} de {repetitions}");
                }
            }

            Console.WriteLine($"\nBorradores con veredicto INESTABLE (cambia entre repeticiones): {unstable.Count} de {cases.Count}");
            foreach (var u in unstable) Console.WriteLine($"  - {u}");
            Console.WriteLine($"Rechazados: media {reports.Average(x => x.FlawedRejected):F1} de {reports[0].FlawedTotal} (mín {reports.Min(x => x.FlawedRejected)} · máx {reports.Max(x => x.FlawedRejected)})");
            Console.WriteLine($"Aprobados:  media {reports.Average(x => x.GoodApproved):F1} de {reports[0].GoodTotal} (mín {reports.Min(x => x.GoodApproved)} · máx {reports.Max(x => x.GoodApproved)})");
            return 0;
        }

        var results = await RunOnceAsync();
        var report = AuditorEvaluation.Evaluate(results);

        Console.WriteLine($"Borradores defectuosos rechazados .... {Pct(report.CatchRate)}  ({report.FlawedRejected}/{report.FlawedTotal})");
        Console.WriteLine($"Borradores buenos aprobados .......... {Pct(report.ApprovalRate)}  ({report.GoodApproved}/{report.GoodTotal})");
        Console.WriteLine($"Rechazados por: solo el código {report.CaughtByAutomaticOnly} · solo el modelo {report.CaughtByModelOnly} · ambos {report.CaughtByBoth}");
        Console.WriteLine($"El defecto señalado coincide con el puesto: {report.DefectTypeMatched}/{report.FlawedRejected}");
        Console.WriteLine($"Incidencias del modelo descartadas por no verificables: {report.DiscardedModelIssues}");

        if (report.FalseAlarms.Count > 0)
        {
            Console.WriteLine("\nFalsas alarmas (borrador bueno rechazado):");
            foreach (var f in report.FalseAlarms) Console.WriteLine($"  - {f}");
        }

        if (report.Missed.Count > 0)
        {
            Console.WriteLine("\nSe escaparon (borrador defectuoso aprobado):");
            foreach (var m in report.Missed) Console.WriteLine($"  - {m}");
        }

        if (verbose)
        {
            Console.WriteLine("\nDetalle por borrador:");
            foreach (var (c, r) in results.OrderBy(x => x.Case.Id, StringComparer.Ordinal))
            {
                var expected = c.ShouldApprove ? "bueno" : $"defectuoso ({c.Defect})";
                Console.WriteLine($"\n  {c.Id} [{expected}] → {(r.Passed ? "APROBADO" : "RECHAZADO")}");
                foreach (var i in r.Issues.OrderByDescending(i => i.Blocking))
                {
                    Console.WriteLine($"      {(i.Blocking ? "✗" : "·")} {i.Type} ({i.Origin}): {Shorten(i.Description, 170)}");
                }
            }
        }

        return 0;
    }

    private static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
