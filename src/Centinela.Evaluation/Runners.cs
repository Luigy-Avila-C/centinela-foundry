using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Evaluation;

/// <summary>
/// Ejecuta un agente sobre todo un conjunto etiquetado. Es la única implementación: la usan tanto los comandos
/// <c>evaluar-*</c> (que imprimen el detalle) como la puerta de evaluación (que solo compara con umbrales).
/// </summary>
public static class GuardRunner
{
    public static async Task<(GuardSample Sample, GuardScan Scan)[]> RunAsync(
        IReadOnlyList<GuardSample> samples, SecurityGuardAgent guard, CancellationToken ct, int parallelism = 4)
    {
        using var gate = new SemaphoreSlim(parallelism);
        return await Task.WhenAll(samples.Select(async s =>
        {
            await gate.WaitAsync(ct);
            try { return (Sample: s, Scan: await guard.ScanAsync(s.Text, ct)); }
            finally { gate.Release(); }
        }));
    }
}

public static class AuditorRunner
{
    /// <param name="passages">Pasajes originales de la empresa, por (documento, sección).</param>
    /// <param name="normChunks">Fragmentos de la norma, por identificador.</param>
    /// <param name="withScope">Si se pasa al auditor el alcance que aplica a cada pasaje (por defecto no, como en la medida base).</param>
    public static async Task<(AuditDraftCase Case, AuditResult Result)[]> RunAsync(
        IReadOnlyList<AuditDraftCase> cases,
        IReadOnlyDictionary<(string Document, string Section), TextChunk> passages,
        IReadOnlyDictionary<string, TextChunk> normChunks,
        string normTitle,
        AuditorAgent auditor,
        bool withScope,
        CancellationToken ct,
        int parallelism = 3)
    {
        using var gate = new SemaphoreSlim(parallelism);
        return await Task.WhenAll(cases.Select(async c =>
        {
            var passage = passages[(c.Finding.Document, c.Finding.Section)];
            var input = new AuditInput(
                AuditorEvaluation.ToAction(c, passage.Id, passage.Text),
                c.Finding.Obligations,
                c.Finding.NormIds,
                c.Finding.NormIds.Select(id => normChunks[id]).ToList(),
                normTitle,
                c.Finding.ProblemQuote,
                c.Finding.ProblemEffect,
                withScope ? c.Finding.Scope : null);

            await gate.WaitAsync(ct);
            try { return (Case: c, Result: await auditor.AuditOneAsync(input, ct)); }
            finally { gate.Release(); }
        }));
    }
}
