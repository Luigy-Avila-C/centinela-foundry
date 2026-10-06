using System.Text.Json;
using Centinela.Domain;

using Centinela.Application;

namespace Centinela.Evaluation;

/// <summary>Un caso etiquetado: una afirmación, los fragmentos que cita y el veredicto correcto.</summary>
public sealed record EvalCase(
    string Id, string Split, Support Expected, string Claim, IReadOnlyList<TextChunk> Cited);

public sealed record EvalOutcome(EvalCase Case, Support Actual, string Reason);

/// <summary>
/// Métricas del verificador. La precisión global engaña (un verificador que aprueba todo saca buena
/// nota si casi todo es correcto), así que se miden por separado los dos errores que importan.
/// </summary>
public sealed record EvalReport(
    int Total,
    int ExactMatches,
    int ShouldFlag,
    int Flagged,
    int UnsupportedExpected,
    int UnsupportedCaught,
    int ShouldPass,
    int FalseAlarms,
    int[,] Confusion)
{
    public double Accuracy => Ratio(ExactMatches, Total);

    /// <summary>
    /// De las afirmaciones que NO deberían pasar (no respaldadas o parciales), cuántas marca el
    /// verificador. Es la métrica crítica: un fallo aquí deja pasar una cita que no respalda.
    /// </summary>
    public double ProblemRecall => Ratio(Flagged, ShouldFlag);

    /// <summary>Recall solo de las completamente no respaldadas, que son las más graves.</summary>
    public double UnsupportedRecall => Ratio(UnsupportedCaught, UnsupportedExpected);

    /// <summary>De las afirmaciones correctas, cuántas marca por error. Mide el ruido para quien revisa.</summary>
    public double FalseAlarmRate => Ratio(FalseAlarms, ShouldPass);

    private static double Ratio(int n, int d) => d == 0 ? double.NaN : (double)n / d;

    /// <summary>Marcar una afirmación = cualquier veredicto distinto de "respaldada".</summary>
    public static EvalReport From(IEnumerable<EvalOutcome> outcomes)
    {
        var list = outcomes.ToList();
        var confusion = new int[3, 3];
        int exact = 0, shouldFlag = 0, flagged = 0, unsExp = 0, unsCaught = 0, shouldPass = 0, falseAlarms = 0;

        foreach (var o in list)
        {
            confusion[(int)o.Case.Expected, (int)o.Actual]++;
            if (o.Case.Expected == o.Actual) exact++;

            if (o.Case.Expected == Support.Supported)
            {
                shouldPass++;
                if (o.Actual != Support.Supported) falseAlarms++;
            }
            else
            {
                shouldFlag++;
                if (o.Actual != Support.Supported) flagged++;
            }

            if (o.Case.Expected == Support.Unsupported)
            {
                unsExp++;
                if (o.Actual == Support.Unsupported) unsCaught++;
            }
        }

        return new EvalReport(list.Count, exact, shouldFlag, flagged, unsExp, unsCaught, shouldPass, falseAlarms, confusion);
    }
}

public static class VerifierEvaluation
{
    /// <summary>Ejecuta el verificador sobre todos los casos, con concurrencia limitada.</summary>
    public static async Task<IReadOnlyList<EvalOutcome>> RunAsync(
        ICitationVerifier verifier, IReadOnlyList<EvalCase> cases, int parallelism, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(parallelism);

        var tasks = cases.Select(async c =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var verdict = await verifier.VerifyAsync(new Claim(c.Claim, c.Cited.Select(x => x.Id).ToList()), c.Cited, ct);
                return new EvalOutcome(c, verdict.Support, verdict.Reason);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    /// <summary>Lee el conjunto de casos. Un fichero mal formado falla con un mensaje claro, no en silencio.</summary>
    public static IReadOnlyList<EvalCase> LoadDataset(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var sources = new Dictionary<string, TextChunk>();
        foreach (var s in root.GetProperty("fuentes").EnumerateObject())
        {
            var v = s.Value;
            sources[s.Name] = new TextChunk(
                v.GetProperty("id").GetString()!,
                v.GetProperty("id").GetString()!.Split('_')[0],
                v.GetProperty("documento").GetString()!,
                v.GetProperty("etiqueta").GetString()!,
                v.GetProperty("texto").GetString()!,
                new DateOnly(2026, 10, 5),
                new Uri("https://www.boe.es/"));
        }

        var cases = new List<EvalCase>();
        foreach (var c in root.GetProperty("casos").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            var cited = c.GetProperty("citas").EnumerateArray().Select(e =>
            {
                var key = e.GetString()!;
                return sources.TryGetValue(key, out var chunk)
                    ? chunk
                    : throw new InvalidOperationException($"El caso {id} cita una fuente inexistente: {key}.");
            }).ToList();

            cases.Add(new EvalCase(
                id,
                c.GetProperty("split").GetString()!,
                ParseExpected(id, c.GetProperty("esperado").GetString()),
                c.GetProperty("afirmacion").GetString()!,
                cited));
        }

        if (cases.Select(c => c.Id).Distinct().Count() != cases.Count)
        {
            throw new InvalidOperationException("El conjunto de evaluación tiene identificadores de caso repetidos.");
        }

        return cases;
    }

    private static Support ParseExpected(string id, string? value) => value switch
    {
        "respaldada" => Support.Supported,
        "parcial" => Support.Partial,
        "no_respaldada" => Support.Unsupported,
        _ => throw new InvalidOperationException($"El caso {id} tiene una etiqueta desconocida: '{value}'."),
    };
}
