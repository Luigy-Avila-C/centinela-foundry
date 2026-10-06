using System.Text.Json;
using Centinela.Domain;

using Centinela.Application;

namespace Centinela.Evaluation;

public enum ImpactExpectation { NotAffected, Affected, Doubtful }

/// <summary>Un pasaje de un documento interno con la respuesta correcta sobre si la norma lo afecta.</summary>
/// <param name="Severity">Gravedad esperada (solo si está afectado).</param>
public sealed record ImpactCase(
    string DocumentId, string Section, string Split, ImpactExpectation Expected,
    ImpactSeverity? Severity, IReadOnlyList<string> Facts, string Note);

public sealed record ImpactEvalReport(
    int TruePositives, int FalsePositives, int FalseNegatives, int TrueNegatives,
    int Doubtful, int DoubtfulFlagged,
    int PositivesRetrieved, int SeverityComparable, int SeverityExact, int SeverityWithinOne,
    IReadOnlyList<string> FalsePositiveCases, IReadOnlyList<string> FalseNegativeCases,
    IReadOnlyList<string> NotRetrievedCases)
{
    public int Positives => TruePositives + FalseNegatives;
    public int Negatives => FalsePositives + TrueNegatives;

    /// <summary>De los pasajes marcados como afectados, cuántos lo están de verdad.</summary>
    public double Precision => Ratio(TruePositives, TruePositives + FalsePositives);

    /// <summary>De los pasajes realmente afectados, cuántos se encontraron. Es lo que no puede fallar.</summary>
    public double Recall => Ratio(TruePositives, Positives);

    /// <summary>De los afectados, cuántos llegaron siquiera al juez: separa fallos de búsqueda de fallos de juicio.</summary>
    public double RetrievalRecall => Ratio(PositivesRetrieved, Positives);

    private static double Ratio(int n, int d) => d == 0 ? double.NaN : (double)n / d;
}

public static class ImpactEvaluation
{
    public static IReadOnlyList<ImpactCase> LoadDataset(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var cases = new List<ImpactCase>();

        foreach (var c in doc.RootElement.GetProperty("casos").EnumerateArray())
        {
            var document = c.GetProperty("documento").GetString()!;
            var section = c.GetProperty("seccion").GetString()!;

            var expected = c.GetProperty("esperado").GetString() switch
            {
                "afectado" => ImpactExpectation.Affected,
                "no_afectado" => ImpactExpectation.NotAffected,
                "dudoso" => ImpactExpectation.Doubtful,
                var other => throw new InvalidOperationException($"El caso {document}/{section} tiene una etiqueta desconocida: '{other}'."),
            };

            ImpactSeverity? severity = !c.TryGetProperty("gravedad", out var g) || g.ValueKind != JsonValueKind.String ? null
                : g.GetString() switch
                {
                    "baja" => ImpactSeverity.Low,
                    "media" => ImpactSeverity.Medium,
                    "alta" => ImpactSeverity.High,
                    var other => throw new InvalidOperationException($"Gravedad desconocida en {document}/{section}: '{other}'."),
                };

            if (expected == ImpactExpectation.Affected && severity is null)
            {
                throw new InvalidOperationException($"El caso afectado {document}/{section} no indica gravedad.");
            }

            cases.Add(new ImpactCase(
                document, section, c.GetProperty("split").GetString()!, expected, severity,
                c.GetProperty("hechos").EnumerateArray().Select(h => h.GetString()!).ToList(),
                c.TryGetProperty("nota", out var n) ? n.GetString() ?? "" : ""));
        }

        var keys = cases.Select(c => (c.DocumentId, c.Section)).ToList();
        if (keys.Distinct().Count() != keys.Count)
        {
            throw new InvalidOperationException("El conjunto de impacto tiene pasajes repetidos.");
        }

        return cases;
    }

    /// <param name="chunks">Todos los pasajes de la empresa, para localizar cada caso por documento y sección.</param>
    public static ImpactEvalReport Evaluate(
        IReadOnlyList<ImpactCase> cases, IReadOnlyList<TextChunk> chunks, ImpactAssessment assessment)
    {
        var findingByPassage = assessment.Findings.Where(f => f.PassageId is not null).ToDictionary(f => f.PassageId!);
        var candidates = assessment.CandidatePassageIds.ToHashSet();

        int tp = 0, fp = 0, fn = 0, tn = 0, doubtful = 0, doubtfulFlagged = 0, retrieved = 0;
        int comparable = 0, exact = 0, withinOne = 0;
        List<string> fpCases = [], fnCases = [], notRetrieved = [];

        foreach (var c in cases)
        {
            // Un caso que no se localiza es un fallo de los datos, no del agente: se avisa en voz alta.
            var chunk = chunks.FirstOrDefault(x => x.DocumentId == c.DocumentId && x.Label == c.Section)
                        ?? throw new InvalidOperationException($"No se encuentra el pasaje «{c.Section}» en el documento {c.DocumentId}.");

            findingByPassage.TryGetValue(chunk.Id, out var finding);
            var flagged = finding is not null;
            var name = $"{c.DocumentId} · {c.Section}";

            switch (c.Expected)
            {
                case ImpactExpectation.Doubtful:
                    doubtful++;
                    if (flagged) doubtfulFlagged++;
                    break;

                case ImpactExpectation.Affected:
                    if (candidates.Contains(chunk.Id)) retrieved++; else notRetrieved.Add(name);

                    if (flagged)
                    {
                        tp++;
                        comparable++;
                        var diff = Math.Abs((int)finding!.Severity - (int)c.Severity!.Value);
                        if (diff == 0) exact++;
                        if (diff <= 1) withinOne++;
                    }
                    else
                    {
                        fn++;
                        fnCases.Add(name);
                    }

                    break;

                default:
                    if (flagged) { fp++; fpCases.Add(name); } else tn++;
                    break;
            }
        }

        return new ImpactEvalReport(tp, fp, fn, tn, doubtful, doubtfulFlagged, retrieved, comparable, exact, withinOne, fpCases, fnCases, notRetrieved);
    }
}
