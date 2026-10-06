using System.Text.Json;
using Centinela.Domain;

using Centinela.Application;

namespace Centinela.Evaluation;

/// <summary>El pasaje afectado sobre el que se escribieron los borradores del conjunto de evaluación.</summary>
/// <param name="ProblemQuote">La frase del pasaje que incumple, tal como la identificaría el evaluador de impacto.</param>
/// <param name="Scope">Por obligación, el fragmento que aplica de verdad al pasaje (si es solo una parte); <c>null</c> si aplica entera.</param>
public sealed record AuditFinding(
    string Id, string Split, string Document, string Section,
    IReadOnlyList<string> Obligations, IReadOnlyList<string> NormIds,
    string? ProblemQuote = null, string? ProblemEffect = null, IReadOnlyList<string>? Scope = null);

/// <summary>Un borrador etiquetado: si debería aprobarse y, si no, qué fallo se le puso a propósito.</summary>
public sealed record AuditDraftCase(
    string Id, AuditFinding Finding, bool ShouldApprove, string? Defect, string Text,
    IReadOnlyList<ObligationCoverage> Coverage, IReadOnlyList<string> NormCitations,
    IReadOnlyList<string> Pending, string Note);

public sealed record AuditEvalReport(
    int GoodTotal, int GoodApproved, int FlawedTotal, int FlawedRejected,
    int DefectTypeMatched, int CaughtByAutomaticOnly, int CaughtByModelOnly, int CaughtByBoth,
    int DiscardedModelIssues,
    IReadOnlyList<string> FalseAlarms, IReadOnlyList<string> Missed)
{
    /// <summary>De los borradores defectuosos, cuántos rechaza el auditor. Es lo que no puede fallar.</summary>
    public double CatchRate => FlawedTotal == 0 ? double.NaN : (double)FlawedRejected / FlawedTotal;

    /// <summary>De los borradores buenos, cuántos aprueba. Mide el ruido para quien revisa.</summary>
    public double ApprovalRate => GoodTotal == 0 ? double.NaN : (double)GoodApproved / GoodTotal;
}

public static class AuditorEvaluation
{
    public static IReadOnlyList<AuditDraftCase> LoadDataset(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var findings = new Dictionary<string, AuditFinding>();
        foreach (var f in root.GetProperty("hallazgos").EnumerateObject())
        {
            var v = f.Value;
            findings[f.Name] = new AuditFinding(
                f.Name,
                v.GetProperty("split").GetString()!,
                v.GetProperty("documento").GetString()!,
                v.GetProperty("seccion").GetString()!,
                v.GetProperty("obligaciones").EnumerateArray().Select(o => o.GetString()!).ToList(),
                v.GetProperty("citas_norma").EnumerateArray().Select(o => o.GetString()!).ToList(),
                v.TryGetProperty("cita_problema", out var pq) && pq.ValueKind == JsonValueKind.String ? pq.GetString() : null,
                v.TryGetProperty("efecto", out var ef) && ef.ValueKind == JsonValueKind.String ? ef.GetString() : null,
                v.TryGetProperty("alcance", out var sc) && sc.ValueKind == JsonValueKind.Array
                    ? sc.EnumerateArray().Select(o => o.GetString()!).ToList()
                    : null);
        }

        var cases = new List<AuditDraftCase>();
        foreach (var d in root.GetProperty("borradores").EnumerateArray())
        {
            var id = d.GetProperty("id").GetString()!;
            var findingId = d.GetProperty("hallazgo").GetString()!;
            var finding = findings.TryGetValue(findingId, out var fnd)
                ? fnd
                : throw new InvalidOperationException($"El borrador {id} pertenece a un hallazgo inexistente: {findingId}.");

            var expected = d.GetProperty("esperado").GetString() switch
            {
                "aprobar" => true,
                "rechazar" => false,
                var other => throw new InvalidOperationException($"El borrador {id} tiene una etiqueta desconocida: '{other}'."),
            };

            var defect = d.TryGetProperty("defecto", out var def) && def.ValueKind == JsonValueKind.String ? def.GetString() : null;
            if (!expected && defect is null) throw new InvalidOperationException($"El borrador defectuoso {id} no indica su defecto.");
            if (expected && defect is not null) throw new InvalidOperationException($"El borrador bueno {id} indica un defecto.");

            var coverage = d.GetProperty("cobertura").EnumerateArray().Select(c =>
            {
                var n = c.GetProperty("obligacion").GetInt32();
                if (n < 1 || n > finding.Obligations.Count)
                {
                    throw new InvalidOperationException($"El borrador {id} cubre una obligación inexistente: {n}.");
                }

                return new ObligationCoverage(
                    finding.Obligations[n - 1], c.GetProperty("como").GetString()!, c.GetProperty("cita").GetString()!);
            }).ToList();

            cases.Add(new AuditDraftCase(
                id, finding, expected, defect, d.GetProperty("texto").GetString()!, coverage,
                d.GetProperty("citas_norma").EnumerateArray().Select(c => c.GetString()!).ToList(),
                d.GetProperty("pendiente").EnumerateArray().Select(c => c.GetString()!).ToList(),
                d.TryGetProperty("nota", out var n2) ? n2.GetString() ?? "" : ""));
        }

        if (cases.Select(c => c.Id).Distinct().Count() != cases.Count)
        {
            throw new InvalidOperationException("El conjunto de borradores tiene identificadores repetidos.");
        }

        return cases;
    }

    /// <summary>Convierte un borrador del conjunto en la acción correctora que auditaría el sistema.</summary>
    public static CorrectiveAction ToAction(AuditDraftCase c, string passageId, string originalText) => new(
        c.Finding.Document, c.Text, c.Note, null, passageId, c.Finding.Section, originalText,
        c.NormCitations, c.Coverage, c.Pending);

    public static AuditEvalReport Evaluate(IEnumerable<(AuditDraftCase Case, AuditResult Result)> results)
    {
        int goodTotal = 0, goodApproved = 0, flawed = 0, rejected = 0, typeMatched = 0, autoOnly = 0, modelOnly = 0, both = 0, discarded = 0;
        List<string> falseAlarms = [], missed = [];

        foreach (var (c, r) in results)
        {
            discarded += r.DiscardedModelIssues;
            var blocking = r.Issues.Where(i => i.Blocking).ToList();

            if (c.ShouldApprove)
            {
                goodTotal++;
                if (r.Passed) goodApproved++;
                else falseAlarms.Add($"{c.Id}: {string.Join(" | ", blocking.Select(i => $"{i.Type} ({i.Origin})"))}");
                continue;
            }

            flawed++;
            if (r.Passed)
            {
                missed.Add($"{c.Id} ({c.Defect})");
                continue;
            }

            rejected++;
            if (blocking.Any(i => i.Type == c.Defect)) typeMatched++;

            var byCode = blocking.Any(i => i.Origin == "automatica");
            var byModel = blocking.Any(i => i.Origin == "modelo");
            if (byCode && byModel) both++;
            else if (byCode) autoOnly++;
            else modelOnly++;
        }

        return new AuditEvalReport(goodTotal, goodApproved, flawed, rejected, typeMatched, autoOnly, modelOnly, both, discarded, falseAlarms, missed);
    }
}
