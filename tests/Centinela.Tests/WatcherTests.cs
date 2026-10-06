using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class WatcherTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // Convención de los dobles: el título dice qué pasa con la publicación.
    private static SourceEntry Entry(string id, string title, DateOnly? date = null) =>
        new(id, "BOE", title, "Ministerio", "1", new Uri($"https://www.boe.es/{id}"), date ?? Today);

    private sealed class Rig
    {
        public InMemoryCaseRepository Repository { get; } = new(new FixedClock());
        public FakeSource Source { get; } = new();
        public FakeAnalyst Analyst { get; } = new();
        public WatcherOptions Options { get; } = new() { Enabled = true, MaxNewCasesPerRun = 2, MaxNewCasesPerDay = 3, MaxScreeningsPerRun = 100, MaxAttempts = 2, LookbackDays = 1 };
        public FakeGuard Guard { get; } = new();
        public FakeScreening Screening { get; } = new();

        public RegulatoryWatcher Watcher() => new(
            Source, Guard, Screening,
            new ComplianceWorkflow(Guard, Screening, Analyst, new FakeImpact(), new FakeDrafter(), new FakeAuditor(), Repository, NullLogger<ComplianceWorkflow>.Instance),
            Repository, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<RegulatoryWatcher>.Instance, new FixedClock());
    }

    [Fact]
    public async Task Irrelevant_publications_are_recorded_so_they_are_not_screened_again()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-1", "nada que ver"));

        var first = await rig.Watcher().RunAsync([Today], false, default);
        var second = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(1, first.Count(WatchOutcome.Irrelevant));
        Assert.Equal(1, second.Count(WatchOutcome.AlreadyKnown));
        Assert.Equal(1, rig.Screening.Calls);
    }

    [Fact]
    public async Task A_relevant_publication_runs_the_full_workflow_and_waits_for_a_person()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-2", "RELEVANTE"));

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(1, report.Count(WatchOutcome.Processed));
        var saved = (await rig.Repository.ListSummariesAsync(null, default)).Single();
        Assert.Equal(CaseStatus.AwaitingHumanApproval, saved.Status);
        Assert.True(saved.Expensive);
    }

    [Fact]
    public async Task The_watcher_never_approves_anything_by_itself()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-3", "RELEVANTE"));

        await rig.Watcher().RunAsync([Today], false, default);

        Assert.DoesNotContain(await rig.Repository.ListSummariesAsync(null, default), s => s.Status is CaseStatus.Approved or CaseStatus.Rejected);
    }

    [Fact]
    public async Task A_text_flagged_by_the_guard_is_blocked_and_never_downloaded()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-4", "RELEVANTE INYECCION"));

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(1, report.Count(WatchOutcome.BlockedByGuard));
        Assert.Equal(0, rig.Source.Downloads);
        Assert.Equal(0, rig.Screening.Calls);
        var saved = (await rig.Repository.ListSummariesAsync(null, default)).Single();
        Assert.Equal(CaseStatus.Failed, saved.Status);
        Assert.False(saved.Expensive);
    }

    [Fact]
    public async Task The_per_run_budget_stops_expensive_cases_and_leaves_the_rest_for_next_time()
    {
        var rig = new Rig();
        foreach (var n in Enumerable.Range(1, 4)) rig.Source.Add(Today, Entry($"BOE-A-{n}0", "RELEVANTE"));

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(2, report.Count(WatchOutcome.Processed));
        Assert.Equal(2, report.Count(WatchOutcome.NoBudget));
        // Lo que no cupo NO se guarda: la siguiente pasada lo volverá a encontrar.
        Assert.Equal(2, (await rig.Repository.ListSummariesAsync(null, default)).Count);
    }

    [Fact]
    public async Task The_daily_budget_counts_what_earlier_runs_already_spent()
    {
        var rig = new Rig { Options = { MaxNewCasesPerRun = 5, MaxNewCasesPerDay = 3 } };
        foreach (var n in Enumerable.Range(1, 5)) rig.Source.Add(Today, Entry($"BOE-A-{n}1", "RELEVANTE"));

        var first = await rig.Watcher().RunAsync([Today], false, default);
        var second = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(3, first.Count(WatchOutcome.Processed));
        Assert.Equal(0, second.Count(WatchOutcome.Processed));
        Assert.Equal(2, second.Count(WatchOutcome.NoBudget));
    }

    [Fact]
    public async Task Cheap_discards_do_not_use_up_the_expensive_budget()
    {
        var rig = new Rig();
        foreach (var n in Enumerable.Range(1, 20)) rig.Source.Add(Today, Entry($"BOE-A-{n}2", "irrelevante"));
        rig.Source.Add(Today, Entry("BOE-A-RELEVANTE", "RELEVANTE"));

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(20, report.Count(WatchOutcome.Irrelevant));
        Assert.Equal(1, report.Count(WatchOutcome.Processed));
    }

    [Fact]
    public async Task Simulation_saves_nothing_and_downloads_nothing()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-5", "RELEVANTE"));
        rig.Source.Add(Today, Entry("BOE-A-6", "irrelevante"));

        var report = await rig.Watcher().RunAsync([Today], true, default);

        Assert.Equal(1, report.Count(WatchOutcome.WouldProcess));
        Assert.Equal(1, report.Count(WatchOutcome.Irrelevant));
        Assert.Empty(await rig.Repository.ListSummariesAsync(null, default));
        Assert.Equal(0, rig.Source.Downloads);
        Assert.Contains("SIMULACIÓN", report.Summary());
    }

    [Fact]
    public async Task A_failed_case_is_retried_a_limited_number_of_times()
    {
        var rig = new Rig();
        rig.Analyst.Throws = true;
        rig.Source.Add(Today, Entry("BOE-A-7", "RELEVANTE"));

        var attempts = new List<WatchReport>();
        for (var i = 0; i < 4; i++) attempts.Add(await rig.Watcher().RunAsync([Today], false, default));

        // Intento 1 y 2 fallan (el flujo guarda un caso Failed cada vez); después se rinde y avisa de que lo mire una persona.
        Assert.Equal(1, attempts[0].Count(WatchOutcome.Processed));
        Assert.Equal(1, attempts[1].Count(WatchOutcome.Processed));
        Assert.Equal(1, attempts[2].Count(WatchOutcome.RetriesExhausted));
        Assert.Equal(1, attempts[3].Count(WatchOutcome.RetriesExhausted));
        Assert.Equal(2, (await rig.Repository.ListSummariesAsync(CaseStatus.Failed, default)).Count);
    }

    [Fact]
    public async Task One_publication_that_blows_up_does_not_stop_the_others()
    {
        var rig = new Rig();
        rig.Source.Add(Today, Entry("BOE-A-8", "RELEVANTE ROTA"));
        rig.Source.Add(Today, Entry("BOE-A-9", "RELEVANTE"));
        rig.Source.FailDownloadFor = "BOE-A-8";

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(1, report.Count(WatchOutcome.Error));
        Assert.Equal(1, report.Count(WatchOutcome.Processed));
    }

    [Fact]
    public async Task A_day_that_fails_to_load_is_reported_and_the_next_one_still_runs()
    {
        var rig = new Rig();
        var yesterday = Today.AddDays(-1);
        rig.Source.FailSummaryFor = Today;
        rig.Source.Add(yesterday, Entry("BOE-A-10", "irrelevante", yesterday));

        var report = await rig.Watcher().RunAsync([Today, yesterday], false, default);

        Assert.Equal(1, report.Count(WatchOutcome.Error));
        Assert.Equal(1, report.Count(WatchOutcome.Irrelevant));
    }

    [Fact]
    public async Task The_screening_cap_limits_how_much_is_screened_in_one_run()
    {
        var rig = new Rig { Options = { MaxScreeningsPerRun = 5 } };
        foreach (var n in Enumerable.Range(1, 9)) rig.Source.Add(Today, Entry($"BOE-A-{n}3", "irrelevante"));

        var report = await rig.Watcher().RunAsync([Today], false, default);

        Assert.Equal(5, report.Count(WatchOutcome.Irrelevant));
        Assert.Equal(4, report.Count(WatchOutcome.NotScreened));
    }

    [Fact]
    public void Default_dates_are_today_and_the_lookback_days_newest_first()
    {
        var dates = new Rig { Options = { LookbackDays = 2 } }.Watcher().DefaultDates();

        Assert.Equal([Today, Today.AddDays(-1), Today.AddDays(-2)], dates);
    }

    // ───── dobles ─────

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeSource : IRegulatorySource
    {
        private readonly Dictionary<DateOnly, List<SourceEntry>> _entries = [];
        public int Downloads { get; private set; }
        public string? FailDownloadFor { get; set; }
        public DateOnly? FailSummaryFor { get; set; }

        public void Add(DateOnly date, SourceEntry entry)
        {
            if (!_entries.TryGetValue(date, out var list)) _entries[date] = list = [];
            list.Add(entry);
        }

        public Task<IReadOnlyList<SourceEntry>> GetEntriesAsync(DateOnly date, CancellationToken ct) =>
            date == FailSummaryFor
                ? throw new HttpRequestException("BOE caído")
                : Task.FromResult<IReadOnlyList<SourceEntry>>(_entries.GetValueOrDefault(date) ?? []);

        public Task<RegulatoryChange> GetFullChangeAsync(SourceEntry entry, CancellationToken ct)
        {
            Downloads++;
            if (entry.SourceId == FailDownloadFor) throw new HttpRequestException("descarga fallida");
            return Task.FromResult(new RegulatoryChange(entry.SourceId, entry.SourceName, entry.Title, entry.Url, entry.Date, "texto completo", []));
        }
    }

    private sealed class FakeGuard : ISecurityGuardAgent
    {
        public Task<GuardResult> InspectAsync(RegulatoryChange change, CancellationToken ct) =>
            Task.FromResult(change.Title.Contains("INYECCION") ? new GuardResult(false, "inyección") : new GuardResult(true, null));
    }

    private sealed class FakeScreening : IScreeningAgent
    {
        public int Calls { get; private set; }

        public Task<bool> IsRelevantAsync(RegulatoryChange change, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(change.Title.Contains("RELEVANTE"));
        }
    }

    private sealed class FakeAnalyst : IRegulatoryAnalystAgent
    {
        public bool Throws { get; set; }

        public Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange c, CancellationToken ct) =>
            Throws ? throw new InvalidOperationException("modelo no disponible") : Task.FromResult(new ChangeAnalysis("resumen", [], []));
    }

    private sealed class FakeImpact : IImpactAgent
    {
        public Task<IReadOnlyList<ImpactFinding>> AssessAsync(ChangeAnalysis a, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ImpactFinding>>([new ImpactFinding("doc-1", "Política", ImpactSeverity.High, "afecta")]);
    }

    private sealed class FakeDrafter : IDrafterAgent
    {
        public Task<IReadOnlyList<CorrectiveAction>> DraftAsync(ComplianceCase c, IReadOnlyList<string> issues, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CorrectiveAction>>([new CorrectiveAction("doc-1", "texto nuevo", "porque sí", null)]);
    }

    private sealed class FakeAuditor : IAuditorAgent
    {
        public Task<AuditVerdict> AuditAsync(ComplianceCase c, CancellationToken ct) =>
            Task.FromResult(new AuditVerdict(true, [], c.Revision));
    }
}
