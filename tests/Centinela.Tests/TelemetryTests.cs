using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centinela.Tests;

public class TelemetryTests
{
    private const string Secret = "TEXTO-CONFIDENCIAL-DE-LA-EMPRESA-12345";

    private static (ActivityListener Listener, ConcurrentBag<Activity> Spans) Listen()
    {
        var spans = new ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, spans);
    }

    private static ComplianceWorkflow Workflow(bool fail = false) => new(
        new Guard(), new Screening(), new Analyst(fail), new Impact(), new Drafter(), new Auditor(),
        new InMemoryCaseRepository(), NullLogger<ComplianceWorkflow>.Instance);

    private static RegulatoryChange Change(string id) => new(
        id, "BOE", Secret, new Uri("https://www.boe.es/x"), new DateOnly(2026, 10, 6), Secret + " texto íntegro " + Secret);

    [Fact]
    public async Task A_case_produces_a_root_span_with_one_child_per_stage_and_its_final_status()
    {
        var (listener, spans) = Listen();
        using var _ = listener;

        var c = await Workflow().RunAsync(Change("TEL-1"), default);

        var root = spans.Single(s => s.OperationName == "caso" && s.GetTagItem(Telemetry.Tag.SourceId) as string == "TEL-1");
        Assert.Equal(c.Id.ToString(), root.GetTagItem(Telemetry.Tag.CaseId));
        Assert.Equal("AwaitingHumanApproval", root.GetTagItem(Telemetry.Tag.Status));

        var stages = spans.Where(s => s.ParentId == root.Id).Select(s => s.OperationName).ToList();
        string[] expected = ["etapa Guardián", "etapa Cribado", "etapa Análisis", "etapa Impacto", "etapa Redacción", "etapa Auditoría"];
        Assert.Equal(expected.Order(), stages.Order());
    }

    [Fact]
    public async Task A_failing_stage_marks_its_span_and_the_root_as_errors_without_leaking_the_message()
    {
        var (listener, spans) = Listen();
        using var _ = listener;

        await Workflow(fail: true).RunAsync(Change("TEL-2"), default);

        var root = spans.Single(s => s.OperationName == "caso" && s.GetTagItem(Telemetry.Tag.SourceId) as string == "TEL-2");
        var stage = spans.Single(s => s.ParentId == root.Id && s.OperationName == "etapa Análisis");
        Assert.Equal(ActivityStatusCode.Error, stage.Status);
        Assert.Equal("InvalidOperationException", stage.StatusDescription);
        Assert.Equal("Failed", root.GetTagItem(Telemetry.Tag.Status));
        Assert.Equal(ActivityStatusCode.Error, root.Status);
    }

    [Fact]
    public async Task No_span_carries_the_text_of_the_norm_or_the_title()
    {
        var (listener, spans) = Listen();
        using var _ = listener;

        await Workflow().RunAsync(Change("TEL-3"), default);

        foreach (var s in spans.Where(s => s.GetTagItem(Telemetry.Tag.SourceId) as string == "TEL-3" || s.OperationName.StartsWith("etapa")))
        {
            Assert.DoesNotContain(Secret, s.DisplayName);
            Assert.All(s.TagObjects, t => Assert.DoesNotContain(Secret, t.Value?.ToString() ?? ""));
            Assert.DoesNotContain(Secret, s.StatusDescription ?? "");
        }
    }

    [Fact]
    public void Recording_model_usage_feeds_the_token_and_call_counters_with_model_stage_and_direction()
    {
        var seen = new ConcurrentBag<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (i, l) => { if (i.Meter.Name == Telemetry.Name) l.EnableMeasurementEvents(i); };
        meter.SetMeasurementEventCallback<long>((i, v, tags, _) => seen.Add((i.Name, v, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        meter.Start();

        using (UsageScope.Begin())
        using (UsageScope.Stage("Etapa-de-prueba"))
        {
            UsageScope.Record("modelo-de-prueba", 120, 30);
        }

        var mine = seen.Where(m => m.Tags.GetValueOrDefault(Telemetry.Tag.Stage) as string == "Etapa-de-prueba").ToList();
        Assert.Contains(mine, m => m.Name == "centinela.tokens" && m.Value == 120 && (string?)m.Tags["direction"] == "input");
        Assert.Contains(mine, m => m.Name == "centinela.tokens" && m.Value == 30 && (string?)m.Tags["direction"] == "output");
        Assert.Contains(mine, m => m.Name == "centinela.model.calls" && m.Value == 1 && (string?)m.Tags[Telemetry.Tag.Model] == "modelo-de-prueba");
    }

    [Fact]
    public async Task Finished_cases_are_counted_by_final_status()
    {
        var seen = new ConcurrentBag<(string Status, long Value)>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (i, l) => { if (i.Meter.Name == Telemetry.Name && i.Name == "centinela.cases") l.EnableMeasurementEvents(i); };
        meter.SetMeasurementEventCallback<long>((_, v, tags, _) =>
            seen.Add((tags.ToArray().Single(t => t.Key == Telemetry.Tag.Status).Value?.ToString() ?? "", v)));
        meter.Start();

        await Workflow().RunAsync(Change("TEL-5"), default);

        Assert.Contains(seen, m => m.Status == "AwaitingHumanApproval" && m.Value == 1);
    }

    [Fact]
    public void Telemetry_is_off_unless_explicitly_enabled()
    {
        var empty = new ConfigurationBuilder().Build();
        var on = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Telemetry:Enabled"] = "true" }).Build();

        Assert.False(Centinela.Infrastructure.TelemetrySetup.IsEnabled(empty));
        Assert.Null(Centinela.Infrastructure.TelemetrySetup.StartStandalone(empty, "x"));
        Assert.True(Centinela.Infrastructure.TelemetrySetup.IsEnabled(on));
    }

    private sealed class Guard : ISecurityGuardAgent
    {
        public Task<GuardResult> InspectAsync(RegulatoryChange c, CancellationToken ct) => Task.FromResult(new GuardResult(true, null));
    }

    private sealed class Screening : IScreeningAgent
    {
        public Task<bool> IsRelevantAsync(RegulatoryChange c, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Analyst(bool fail) : IRegulatoryAnalystAgent
    {
        public Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange c, CancellationToken ct) =>
            fail ? throw new InvalidOperationException(Secret + " mensaje con texto") : Task.FromResult(new ChangeAnalysis("r", [], []));
    }

    private sealed class Impact : IImpactAgent
    {
        public Task<IReadOnlyList<ImpactFinding>> AssessAsync(ChangeAnalysis a, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ImpactFinding>>([new ImpactFinding("d", "D", ImpactSeverity.High, "x")]);
    }

    private sealed class Drafter : IDrafterAgent
    {
        public Task<IReadOnlyList<CorrectiveAction>> DraftAsync(ComplianceCase c, IReadOnlyList<string> i, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CorrectiveAction>>([new CorrectiveAction("d", "t", "j", null)]);
    }

    private sealed class Auditor : IAuditorAgent
    {
        public Task<AuditVerdict> AuditAsync(ComplianceCase c, CancellationToken ct) => Task.FromResult(new AuditVerdict(true, [], c.Revision));
    }
}
