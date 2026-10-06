using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Centinela.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Centinela.Cli;

/// <summary>Decisión del medidor sobre un hecho en un control, guardada para poder auditarla después.</summary>
internal sealed record ControlDecision(
    string Judge, string FactId, string FactText, bool Covered,
    IReadOnlyList<string> Pointed, string Reason, int Removed);

/// <summary>
/// <c>centinela control-medidor</c>: valida el medidor de cobertura en el régimen real (cientos de
/// afirmaciones en el prompt), donde la calibración con 1 o 2 afirmaciones no basta.
/// <list type="bullet">
/// <item><b>negativo</b>: para cada hecho se QUITAN las afirmaciones que citan su artículo y se vuelve a
/// preguntar. Lo correcto es «no cubierto»; lo que siga saliendo «cubierto» es una fuga (legítima si otro
/// artículo dice lo mismo, laxitud si no).</item>
/// <item><b>positivo</b>: se pregunta con TODAS las afirmaciones. Mide la sensibilidad: un medidor que
/// siempre dijera «no» también pasaría el control negativo, así que hay que medir las dos cosas.</item>
/// </list>
/// <c>--desde</c>/<c>--hasta</c> (1-based, por orden en la referencia) permiten ajustar con unos hechos y
/// validar con otros.
/// </summary>
internal static class MatcherControl
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var path = args[0];
        var referencePath = Path.Combine("evaluaciones", "cobertura-orden-hac-1028-2026.json");
        var judgeNames = new[] { "gpt-4.1", "gpt-5.1" };
        var runIndex = 1;
        var negative = true;
        var strict = true;
        int from = 1, to = int.MaxValue;

        for (var i = 1; i < args.Length - 1; i += 2)
        {
            var v = args[i + 1];
            switch (args[i])
            {
                case "--ejecucion" when int.TryParse(v, out var n) && n > 0: runIndex = n; break;
                case "--modo" when v is "negativo" or "positivo": negative = v == "negativo"; break;
                case "--matcher" when v is "estricto" or "laxo": strict = v == "estricto"; break;
                case "--desde" when int.TryParse(v, out var d) && d > 0: from = d; break;
                case "--hasta" when int.TryParse(v, out var h) && h > 0: to = h; break;
                case "--referencia": referencePath = v; break;
                case "--jueces": judgeNames = v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                default: return Fail($"Argumento no reconocido: {args[i]} {v}");
            }
        }

        var reference = CoverageReference.Load(await File.ReadAllTextAsync(referencePath, ct));
        var facts = reference.Facts.Skip(from - 1).Take(to - from + 1).ToList();
        if (facts.Count == 0) return Fail("El rango de hechos está vacío.");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var runs = doc.RootElement.GetProperty("Runs").EnumerateArray().ToList();
        if (runIndex > runs.Count) return Fail($"El resultado solo tiene {runs.Count} ejecuciones.");

        var claims = runs[runIndex - 1].GetProperty("Claims").EnumerateArray()
            .Select(c => (Text: c.GetProperty("Text").GetString()!,
                          Cites: c.GetProperty("Citations").EnumerateArray().Select(x => x.GetString()!).ToList()))
            .ToList();

        var llm = services.GetRequiredService<ILanguageModel>();
        ICoverageMatcher matcher = strict ? new StrictCoverageMatcherAgent(llm) : new CoverageMatcherAgent(llm);

        Console.WriteLine(
            $"Control {(negative ? "negativo" : "positivo")} · medidor {(strict ? "estricto" : "laxo")} · ejecución {runIndex} · " +
            $"hechos {facts[0].Id}–{facts[^1].Id} ({facts.Count}) · {claims.Count} afirmaciones · ≈{facts.Count * judgeNames.Length} llamadas\n");

        using var gate = new SemaphoreSlim(4);
        var decisions = new List<ControlDecision>();

        foreach (var judge in judgeNames)
        {
            var tasks = facts.Select(async fact =>
            {
                // Negativo: sin las afirmaciones que citan el artículo del hecho, el análisis ya no puede recogerlo.
                var kept = negative
                    ? claims.Where(c => !c.Cites.Contains(fact.ChunkId)).Select(c => c.Text).ToList()
                    : claims.Select(c => c.Text).ToList();

                await gate.WaitAsync(ct);
                try
                {
                    var m = await matcher.CoversAsync(judge, fact.Text, kept, ct);
                    return new ControlDecision(judge, fact.Id, fact.Text, m.Covered,
                        m.ClaimNumbers.Select(n => kept[n - 1]).ToList(), m.Reason, claims.Count - kept.Count);
                }
                finally { gate.Release(); }
            });

            decisions.AddRange(await Task.WhenAll(tasks));
        }

        var outDir = Path.Combine("evaluaciones", "resultados");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir,
            $"control-{(negative ? "neg" : "pos")}-{(strict ? "estricto" : "laxo")}-ej{runIndex}-{facts[0].Id}-{facts[^1].Id}-{DateTime.Now:HHmmss}.json");
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(decisions, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }), ct);

        foreach (var judge in judgeNames)
        {
            var mine = decisions.Where(d => d.Judge == judge).ToList();
            var covered = mine.Count(d => d.Covered);
            Console.WriteLine(negative
                ? $"{judge}: FUGAS {covered}/{mine.Count} ({Pct(covered, mine.Count)}) — dicen «cubierto» sin el artículo que lo contiene"
                : $"{judge}: SENSIBILIDAD {covered}/{mine.Count} ({Pct(covered, mine.Count)}) — hechos que reconoce con todas las afirmaciones");
        }

        var show = negative ? decisions.Where(d => d.Covered) : decisions.Where(d => !d.Covered);
        Console.WriteLine(negative
            ? "\nFugas a revisar a mano (¿lo dice de verdad otro artículo, o hay laxitud?):"
            : "\nHechos NO reconocidos (¿falta de verdad en el análisis, o el medidor es demasiado duro?):");

        foreach (var d in show.OrderBy(d => d.FactId, StringComparer.Ordinal).ThenBy(d => d.Judge, StringComparer.Ordinal))
        {
            Console.WriteLine($"\n  [{d.Judge}] {d.FactId} — {Shorten(d.FactText, 150)}");
            foreach (var p in d.Pointed.Take(2)) Console.WriteLine($"      señala: {Shorten(p, 180)}");
            Console.WriteLine($"      {Shorten(d.Reason, 230)}");
        }

        Console.WriteLine($"\nDecisiones guardadas en: {outFile}");
        return 0;
    }

    /// <summary>
    /// <c>centinela calibrar-medidor [--matcher estricto|laxo] [--jueces a,b]</c>: pasa al medidor los casos
    /// de calibración (listas de 1 o 2 afirmaciones con respuesta conocida). Hace falta comprobarlo al
    /// endurecer el medidor: uno más estricto podría dejar de reconocer paráfrasis legítimas.
    /// </summary>
    public static async Task<int> CalibrateAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var referencePath = Path.Combine("evaluaciones", "cobertura-orden-hac-1028-2026.json");
        var judgeNames = new[] { "gpt-4.1", "gpt-5.1" };
        var strict = true;

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var v = args[i + 1];
            switch (args[i])
            {
                case "--matcher" when v is "estricto" or "laxo": strict = v == "estricto"; break;
                case "--referencia": referencePath = v; break;
                case "--jueces": judgeNames = v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                default: return Fail($"Argumento no reconocido: {args[i]} {v}");
            }
        }

        var reference = CoverageReference.Load(await File.ReadAllTextAsync(referencePath, ct));
        var llm = services.GetRequiredService<ILanguageModel>();
        ICoverageMatcher matcher = strict ? new StrictCoverageMatcherAgent(llm) : new CoverageMatcherAgent(llm);

        Console.WriteLine($"Calibración del medidor {(strict ? "estricto" : "laxo")}: {reference.Calibration.Count} casos × {judgeNames.Length} jueces\n");

        foreach (var judge in judgeNames)
        {
            var results = await Task.WhenAll(reference.Calibration.Select(async item =>
                (item, match: await matcher.CoversAsync(judge, item.FactText, item.Claims, ct))));

            var wrong = results.Where(r => r.match.Covered != r.item.Expected).ToList();
            Console.WriteLine($"  {judge}: {results.Length - wrong.Count}/{results.Length} correctos");
            foreach (var w in wrong)
            {
                Console.WriteLine($"    ✗ {w.item.Id}: esperado {(w.item.Expected ? "cubierto" : "no cubierto")}, obtenido {(w.match.Covered ? "cubierto" : "no cubierto")} — {Shorten(w.match.Reason, 200)}");
            }
        }

        return 0;
    }

    private static string Pct(int n, int d) => ((double)n / d).ToString("P0", CultureInfo.InvariantCulture);

    private static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
