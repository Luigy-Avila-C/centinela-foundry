using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centinela.Tests;

public class UsageScopeTests
{
    [Fact]
    public void Calls_outside_any_scope_are_ignored()
    {
        UsageScope.Record("gpt-4.1", 100, 50);

        Assert.Null(UsageScope.Current);
    }

    [Fact]
    public void A_scope_adds_up_calls_and_tokens_per_stage_and_model()
    {
        using var scope = UsageScope.Begin();
        using (UsageScope.Stage("Análisis"))
        {
            UsageScope.Record("gpt-4.1", 100, 10);
            UsageScope.Record("gpt-4.1", 200, 20);
            UsageScope.Record("gpt-5.1", 50, 5);
        }

        using (UsageScope.Stage("Auditoría")) UsageScope.Record("gpt-5.1", 7, 3);

        var snap = scope.Snapshot();
        Assert.Equal(new ModelUsage("Análisis", "gpt-4.1", 2, 300, 30), snap.Single(u => u is { Stage: "Análisis", Model: "gpt-4.1" }));
        Assert.Equal(new ModelUsage("Auditoría", "gpt-5.1", 1, 7, 3), snap.Single(u => u.Stage == "Auditoría"));
        Assert.Equal(3, snap.Count);
    }

    [Fact]
    public void An_inner_scope_also_counts_in_the_outer_one_but_not_the_other_way_round()
    {
        using var outer = UsageScope.Begin();
        UsageScope.Record("m", 1, 1);
        using (var inner = UsageScope.Begin())
        {
            UsageScope.Record("m", 10, 10);
            Assert.Equal(10, inner.Snapshot().Sum(u => u.InputTokens));
        }

        Assert.Equal(11, outer.Snapshot().Sum(u => u.InputTokens));
    }

    [Fact]
    public async Task Parallel_calls_are_all_counted_in_the_same_scope()
    {
        using var scope = UsageScope.Begin();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(async _ =>
        {
            await Task.Yield();
            UsageScope.Record("m", 3, 2);
        }));

        var total = scope.Snapshot().Single();
        Assert.Equal((200, 600L, 400L), (total.Calls, total.InputTokens, total.OutputTokens));
    }
}

public class WorkflowUsageTests
{
    private static ComplianceWorkflow Workflow(ICaseRepository repo, ISecurityGuardAgent? guard = null) => new(
        guard ?? new G(), new S(), new A(), new I(), new D(), new U(), repo, NullLogger<ComplianceWorkflow>.Instance);

    [Fact]
    public async Task A_case_stores_its_usage_by_stage_and_it_survives_persistence()
    {
        var repo = new InMemoryCaseRepository();

        var c = await Workflow(repo).RunAsync(CaseFixtures.Change(), default);
        var reloaded = (await repo.GetAsync(c.Id, default))!;

        Assert.Equal(c.Usage.Select(u => u.ToString()), reloaded.Usage.Select(u => u.ToString()));
        Assert.Equal(["Análisis", "Auditoría", "Cribado", "Impacto", "Redacción"], reloaded.Usage.Select(u => u.Stage).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task The_stage_is_set_before_the_call_so_a_record_before_the_first_await_is_attributed_correctly()
    {
        var c = await Workflow(new InMemoryCaseRepository(), new EagerGuard()).RunAsync(CaseFixtures.Change(), default);

        Assert.DoesNotContain(c.Usage, u => u.Stage == "(sin etapa)");
        Assert.Contains(c.Usage, u => u is { Stage: "Guardián", Model: "gpt-4.1-mini" });
    }

    [Fact]
    public async Task Two_cases_do_not_mix_their_usage()
    {
        var repo = new InMemoryCaseRepository();
        var workflow = Workflow(repo);

        var first = await workflow.RunAsync(CaseFixtures.Change(), default);
        var second = await workflow.RunAsync(CaseFixtures.Change(), default);

        Assert.Equal(first.Usage.Sum(u => u.InputTokens), second.Usage.Sum(u => u.InputTokens));
    }

    private sealed class G : ISecurityGuardAgent
    {
        public Task<GuardResult> InspectAsync(RegulatoryChange c, CancellationToken ct) => Task.FromResult(new GuardResult(true, null));
    }

    // Registra antes de su primer await: justo el caso que rompería una atribución hecha después de arrancar la llamada.
    private sealed class EagerGuard : ISecurityGuardAgent
    {
        public async Task<GuardResult> InspectAsync(RegulatoryChange c, CancellationToken ct)
        {
            UsageScope.Record("gpt-4.1-mini", 5, 5);
            await Task.Yield();
            return new GuardResult(true, null);
        }
    }

    private sealed class S : IScreeningAgent
    {
        public Task<bool> IsRelevantAsync(RegulatoryChange c, CancellationToken ct) { UsageScope.Record("gpt-4.1-mini", 10, 1); return Task.FromResult(true); }
    }

    private sealed class A : IRegulatoryAnalystAgent
    {
        public Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange c, CancellationToken ct) { UsageScope.Record("gpt-4.1", 1000, 100); return Task.FromResult(new ChangeAnalysis("r", [], [])); }
    }

    private sealed class I : IImpactAgent
    {
        public Task<IReadOnlyList<ImpactFinding>> AssessAsync(ChangeAnalysis a, CancellationToken ct)
        {
            UsageScope.Record("gpt-5.1", 5, 5);
            return Task.FromResult<IReadOnlyList<ImpactFinding>>([new ImpactFinding("d", "D", ImpactSeverity.High, "x")]);
        }
    }

    private sealed class D : IDrafterAgent
    {
        public Task<IReadOnlyList<CorrectiveAction>> DraftAsync(ComplianceCase c, IReadOnlyList<string> i, CancellationToken ct)
        {
            UsageScope.Record("gpt-4.1", 3, 2);
            return Task.FromResult<IReadOnlyList<CorrectiveAction>>([new CorrectiveAction("d", "t", "j", null)]);
        }
    }

    private sealed class U : IAuditorAgent
    {
        public Task<AuditVerdict> AuditAsync(ComplianceCase c, CancellationToken ct)
        {
            UsageScope.Record("gpt-5.1", 4, 4);
            return Task.FromResult(new AuditVerdict(true, [], c.Revision));
        }
    }
}

public class CostEstimatorTests
{
    private static readonly PricingOptions Pricing = new();

    [Fact]
    public void Cost_is_tokens_times_the_per_million_price()
    {
        // 1M de entrada a 2 USD + 0,5M de salida a 8 USD = 2 + 4 = 6 USD
        var cost = CostEstimator.Estimate([new ModelUsage("x", "gpt-4.1", 10, 1_000_000, 500_000)], Pricing);

        Assert.Equal(6.00m, cost.Usd);
        Assert.Empty(cost.UnpricedModels);
    }

    [Fact]
    public void A_model_without_a_price_is_reported_and_adds_nothing()
    {
        var cost = CostEstimator.Estimate(
            [new ModelUsage("x", "modelo-nuevo", 1, 1_000_000, 1_000_000), new ModelUsage("x", "gpt-4.1-mini", 1, 1_000_000, 0)], Pricing);

        Assert.Equal(0.40m, cost.Usd);
        Assert.Equal(["modelo-nuevo"], cost.UnpricedModels);
    }

    [Fact]
    public void Prices_can_be_overridden()
    {
        var custom = new PricingOptions();
        custom.Models["gpt-4.1"] = new ModelPrice { InputPerMillion = 1m, OutputPerMillion = 1m };

        Assert.Equal(2m, CostEstimator.Estimate([new ModelUsage("x", "gpt-4.1", 1, 1_000_000, 1_000_000)], custom).Usd);
    }

    [Fact]
    public void The_report_has_a_row_per_stage_and_model_a_total_and_the_disclaimer()
    {
        var text = UsageReport.Format(
            [new ModelUsage("Análisis", "gpt-4.1", 2, 1000, 100), new ModelUsage("Auditoría", "gpt-5.1", 1, 500, 50)], Pricing);

        Assert.Contains("Análisis", text);
        Assert.Contains("TOTAL", text);
        Assert.Contains("NO es tu factura", text);
    }

    [Fact]
    public void An_empty_report_says_so()
    {
        Assert.Equal("(sin consumo registrado)", UsageReport.Format([], Pricing));
    }
}
