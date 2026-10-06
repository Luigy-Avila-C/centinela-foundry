using Centinela.Domain;
using Microsoft.Extensions.Logging;

namespace Centinela.Application;

/// <summary>
/// Orquestador determinista. La secuencia de pasos es código, no un LLM: así el flujo es
/// reproducible, auditable y barato. Los modelos solo hacen el trabajo que requiere lenguaje.
/// </summary>
public sealed class ComplianceWorkflow(
    ISecurityGuardAgent guard,
    IScreeningAgent screening,
    IRegulatoryAnalystAgent analyst,
    IImpactAgent impact,
    IDrafterAgent drafter,
    IAuditorAgent auditor,
    ICaseRepository repository,
    ILogger<ComplianceWorkflow> logger)
{
    /// <summary>
    /// Procesa un cambio normativo hasta dejarlo listo para aprobación humana (o descartado).
    /// Nunca aprueba por su cuenta: la decisión final es siempre de una persona.
    /// </summary>
    public async Task<ComplianceCase> RunAsync(RegulatoryChange change, CancellationToken ct)
    {
        var complianceCase = new ComplianceCase(change);

        // Un tramo raíz por caso; cada etapa y cada llamada a un modelo cuelgan de él. Solo identificadores y estados.
        using var root = Telemetry.Source.StartActivity("caso");
        root?.SetTag(Telemetry.Tag.CaseId, complianceCase.Id.ToString());
        root?.SetTag(Telemetry.Tag.SourceId, change.SourceId);

        // Todo lo que gaste este caso en modelos se apunta en él, por etapa, para saber cuánto cuesta cada cosa.
        using var usage = UsageScope.Begin();

        try
        {
            // 1. Seguridad primero: el texto viene de internet y puede traer instrucciones ocultas.
            var guardResult = await Stage("Guardián", () => guard.InspectAsync(change, ct));
            if (!guardResult.IsSafe)
            {
                complianceCase.Fail($"Bloqueado por el guardián: {guardResult.Reason}");
                return await SaveAsync(complianceCase, ct);
            }

            // 2. Cribado barato: la mayoría de las publicaciones no nos afectan.
            complianceCase.MarkScreened(await Stage("Cribado", () => screening.IsRelevantAsync(change, ct)));
            if (complianceCase.Status == CaseStatus.Discarded)
            {
                return await SaveAsync(complianceCase, ct);
            }

            // 3. Análisis normativo con citas.
            complianceCase.SetAnalysis(await Stage("Análisis", () => analyst.AnalyzeAsync(change, ct)));

            // 4. Impacto sobre los documentos de la empresa.
            complianceCase.SetImpact(await Stage("Impacto", () => impact.AssessAsync(complianceCase.Analysis!, ct)));
            if (complianceCase.Status == CaseStatus.Discarded)
            {
                return await SaveAsync(complianceCase, ct);
            }

            // 5. Bucle redactor-auditor, acotado por ComplianceCase.MaxRevisions.
            IReadOnlyList<string> issues = [];
            while (complianceCase.Status is CaseStatus.ImpactAssessed or CaseStatus.RevisionRequested)
            {
                complianceCase.SetDraft(await Stage("Redacción", () => drafter.DraftAsync(complianceCase, issues, ct)));
                var verdict = await Stage("Auditoría", () => auditor.AuditAsync(complianceCase, ct));
                complianceCase.ApplyAudit(verdict);
                issues = verdict.Issues;
            }

            return await SaveAsync(complianceCase, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un caso fallido se registra; no se pierde ni se deja a medias en silencio.
            logger.LogError(ex, "Fallo procesando el cambio {SourceId}", change.SourceId);
            complianceCase.Fail(ex.Message);
            return await SaveAsync(complianceCase, ct);
        }
    }

    // Atribuye a una etapa lo que gaste la llamada (las etapas corren una detrás de otra, así que no se mezclan).
    private static async Task<T> Stage<T>(string name, Func<Task<T>> call)
    {
        // La etapa se fija ANTES de empezar la llamada: un agente puede llamar al modelo antes de su primer await.
        using var span = Telemetry.Source.StartActivity($"etapa {name}");
        span?.SetTag(Telemetry.Tag.Stage, name);
        try
        {
            using (UsageScope.Stage(name)) return await call();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, e.GetType().Name);
            throw;
        }
    }

    private async Task<ComplianceCase> SaveAsync(ComplianceCase complianceCase, CancellationToken ct)
    {
        if (UsageScope.Current is { } scope) complianceCase.SetUsage(scope.Snapshot());

        var status = complianceCase.Status.ToString();
        System.Diagnostics.Activity.Current?.SetTag(Telemetry.Tag.Status, status);
        if (complianceCase.Status == CaseStatus.Failed) System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "caso fallido");
        Telemetry.Cases.Add(1, new KeyValuePair<string, object?>(Telemetry.Tag.Status, status));

        await repository.SaveAsync(complianceCase, ct);
        return complianceCase;
    }
}
