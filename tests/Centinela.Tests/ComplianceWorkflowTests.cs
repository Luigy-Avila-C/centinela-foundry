using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centinela.Tests;

/// <summary>
/// Pruebas del orquestador con agentes falsos: no se llama a ningún modelo, así que son
/// rápidas, gratuitas y deterministas.
/// </summary>
public class ComplianceWorkflowTests
{
    private static readonly RegulatoryChange Change = new(
        "BOE-A-2026-0001", "BOE", "Cambio de prueba",
        new Uri("https://www.boe.es/ejemplo"), new DateOnly(2026, 10, 1), "texto");

    [Fact]
    public async Task Relevant_change_ends_awaiting_human_approval()
    {
        var result = await Build(auditPasses: true).RunAsync(Change, default);

        Assert.Equal(CaseStatus.AwaitingHumanApproval, result.Status);
        Assert.Equal(1, result.Revision);
        Assert.Single(result.Actions);
    }

    [Fact]
    public async Task Irrelevant_change_is_discarded_without_analysis()
    {
        var result = await Build(relevant: false).RunAsync(Change, default);

        Assert.Equal(CaseStatus.Discarded, result.Status);
        Assert.Null(result.Analysis);
    }

    [Fact]
    public async Task Unsafe_source_is_blocked_before_any_other_agent()
    {
        var result = await Build(safe: false).RunAsync(Change, default);

        Assert.Equal(CaseStatus.Failed, result.Status);
        Assert.Null(result.Analysis);
    }

    [Fact]
    public async Task Failing_audit_escalates_to_a_human_after_max_revisions()
    {
        var result = await Build(auditPasses: false).RunAsync(Change, default);

        // Nunca se queda en bucle: tras MaxRevisions rondas decide una persona.
        Assert.Equal(CaseStatus.AwaitingHumanApproval, result.Status);
        Assert.Equal(ComplianceCase.MaxRevisions, result.Revision);
    }

    [Fact]
    public async Task Agent_exception_leaves_a_failed_case_not_a_half_done_one()
    {
        var result = await Build(analystThrows: true).RunAsync(Change, default);

        Assert.Equal(CaseStatus.Failed, result.Status);
    }

    [Fact]
    public void Approving_a_case_that_is_not_awaiting_approval_is_rejected()
    {
        var complianceCase = new ComplianceCase(Change);

        Assert.Throws<InvalidOperationException>(() => complianceCase.Approve("revisor"));
    }

    private static ComplianceWorkflow Build(
        bool safe = true, bool relevant = true, bool auditPasses = true, bool analystThrows = false) =>
        new(new FakeGuard(safe), new FakeScreening(relevant), new FakeAnalyst(analystThrows),
            new FakeImpact(), new FakeDrafter(), new FakeAuditor(auditPasses),
            new Centinela.Infrastructure.Persistence.InMemoryCaseRepository(), NullLogger<ComplianceWorkflow>.Instance);

    private sealed class FakeGuard(bool safe) : ISecurityGuardAgent
    {
        public Task<GuardResult> InspectAsync(RegulatoryChange c, CancellationToken ct) =>
            Task.FromResult(new GuardResult(safe, safe ? null : "prompt injection"));
    }

    private sealed class FakeScreening(bool relevant) : IScreeningAgent
    {
        public Task<bool> IsRelevantAsync(RegulatoryChange c, CancellationToken ct) =>
            Task.FromResult(relevant);
    }

    private sealed class FakeAnalyst(bool throws) : IRegulatoryAnalystAgent
    {
        public Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange c, CancellationToken ct) =>
            throws
                ? throw new InvalidOperationException("modelo no disponible")
                : Task.FromResult(new ChangeAnalysis("resumen", ["art. 1"], ["BOE"]));
    }

    private sealed class FakeImpact : IImpactAgent
    {
        public Task<IReadOnlyList<ImpactFinding>> AssessAsync(ChangeAnalysis a, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ImpactFinding>>(
                [new ImpactFinding("doc-1", "Política", ImpactSeverity.High, "afecta")]);
    }

    private sealed class FakeDrafter : IDrafterAgent
    {
        public Task<IReadOnlyList<CorrectiveAction>> DraftAsync(
            ComplianceCase c, IReadOnlyList<string> issues, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CorrectiveAction>>(
                [new CorrectiveAction("doc-1", "texto nuevo", "porque sí", null)]);
    }

    private sealed class FakeAuditor(bool passes) : IAuditorAgent
    {
        public Task<AuditVerdict> AuditAsync(ComplianceCase c, CancellationToken ct) =>
            Task.FromResult(new AuditVerdict(passes, passes ? [] : ["falta cita"], c.Revision));
    }
}
