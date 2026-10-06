using System.Text.Json;
using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Persistence;

namespace Centinela.Tests;

/// <summary>Casos de prueba reutilizables por las pruebas de persistencia y de la API.</summary>
internal static class CaseFixtures
{
    public static RegulatoryChange Change(bool withSections = true) => new(
        "BOE-A-2026-1", "BOE", "Orden de prueba", new Uri("https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1"),
        new DateOnly(2026, 10, 1), new string('x', 5000),
        withSections
            ?
            [
                new TextChunk("N_1", "BOE-A-2026-1", "Orden de prueba", "Artículo 1", "Texto del artículo 1.", new DateOnly(2026, 10, 1), new Uri("https://www.boe.es/")),
                new TextChunk("N_2", "BOE-A-2026-1", "Orden de prueba", "Artículo 2", "Texto que nadie cita.", new DateOnly(2026, 10, 1), new Uri("https://www.boe.es/")),
            ]
            : null);

    /// <summary>Un caso que llega a «pendiente de aprobación». <paramref name="auditPasses"/> false lo escala; <paramref name="pending"/> añade datos por completar.</summary>
    public static ComplianceCase AwaitingApproval(bool auditPasses = true, bool pending = false)
    {
        var c = new ComplianceCase(Change());
        c.MarkScreened(true);
        c.SetAnalysis(new ChangeAnalysis("Resumen", ["Artículo 1"], ["N_1"],
            [new Claim("La empresa debe X.", ["N_1"])], [new ClaimVerdict(new Claim("La empresa debe X.", ["N_1"]), Support.Supported, "ok")]));
        c.SetImpact([new ImpactFinding("doc-01", "Política", ImpactSeverity.High, "Incumple", "doc-01_001", "1. Sección", "frase mala",
            ["N_1"], ["La empresa debe X."], "Cambiar", "Texto original.", "incumple", ["debe X"])]);

        for (var round = 1; round <= (auditPasses ? 1 : ComplianceCase.MaxRevisions); round++)
        {
            c.SetDraft([new CorrectiveAction("doc-01", "Texto nuevo con [COMPLETAR: responsable].", "Se añade X", null, "doc-01_001", "1. Sección",
                "Texto original.", ["N_1"], [new ObligationCoverage("La empresa debe X.", "añadido", "Texto nuevo")],
                pending ? ["responsable"] : [])]);
            c.ApplyAudit(new AuditVerdict(auditPasses, auditPasses ? [] : ["falta algo"], c.Revision));
        }

        return c;
    }
}

public class SnapshotTests
{
    [Fact]
    public void A_restored_case_is_equivalent_to_the_original()
    {
        var original = CaseFixtures.AwaitingApproval(pending: true);

        var restored = ComplianceCase.Restore(original.ToSnapshot());

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.Revision, restored.Revision);
        Assert.Equal(original.Log, restored.Log);
        Assert.Equal(original.Actions, restored.Actions);
        Assert.Equal(original.Findings, restored.Findings);
    }

    [Fact]
    public void A_restored_case_still_enforces_the_state_machine()
    {
        var restored = ComplianceCase.Restore(CaseFixtures.AwaitingApproval().ToSnapshot());

        // Ya está pendiente de aprobación: no se puede volver a redactar.
        Assert.Throws<InvalidOperationException>(() => restored.SetDraft([]));
    }

    [Fact]
    public void The_snapshot_is_a_copy_so_it_cannot_be_used_to_change_the_case()
    {
        var c = CaseFixtures.AwaitingApproval();
        var snapshot = c.ToSnapshot();

        c.Approve("revisor");

        Assert.Equal(CaseStatus.AwaitingHumanApproval, snapshot.Status);
        Assert.NotEqual(snapshot.Log.Count, c.Log.Count);
    }

    [Fact]
    public void Json_round_trip_preserves_the_document()
    {
        var doc = CaseDocument.From(CaseFixtures.AwaitingApproval(pending: true), new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(doc, CaseJson.Options);
        var back = JsonSerializer.Deserialize<CaseDocument>(json, CaseJson.Options)!;

        Assert.Equal(doc.Id, back.Id);
        Assert.Equal(CaseStatus.AwaitingHumanApproval, back.Status);
        // Los registros comparan listas por referencia: se comprueba que el contenido sea idéntico volviendo a serializar.
        Assert.Equal(json, JsonSerializer.Serialize(back, CaseJson.Options));
        Assert.Equal(doc.Snapshot.Change.PublishedOn, back.Snapshot.Change.PublishedOn);
        Assert.Contains("\"id\":\"", json);
        Assert.Contains("\"status\":\"AwaitingHumanApproval\"", json);
    }

    [Fact]
    public void The_document_keeps_only_the_cited_norm_fragments_and_drops_the_full_text()
    {
        var doc = CaseDocument.From(CaseFixtures.AwaitingApproval(), DateTimeOffset.UtcNow);

        Assert.Equal("", doc.Snapshot.Change.RawText);
        Assert.Equal(["N_1"], doc.Snapshot.Change.Sections!.Select(s => s.Id));
    }

    [Fact]
    public void Summary_fields_are_denormalized_for_listing()
    {
        var doc = CaseDocument.From(CaseFixtures.AwaitingApproval(auditPasses: false, pending: true), DateTimeOffset.UtcNow);

        Assert.True(doc.Escalated);
        Assert.Equal(1, doc.PendingData);
        Assert.Equal(1, doc.Findings);
        Assert.Equal("Orden de prueba", doc.Title);
    }
}

public class ApprovalRulesTests
{
    [Fact]
    public void A_clean_case_can_be_approved_without_acknowledgements()
    {
        var c = CaseFixtures.AwaitingApproval();

        c.Approve("Ana");

        Assert.Equal(CaseStatus.Approved, c.Status);
        Assert.Contains("Aprobado por Ana", c.Log[^1]);
    }

    [Fact]
    public void An_escalated_case_cannot_be_approved_without_acknowledging_it()
    {
        var c = CaseFixtures.AwaitingApproval(auditPasses: false);

        var e = Assert.Throws<RiskNotAcknowledgedException>(() => c.Approve("Ana"));

        Assert.Contains(e.Risks, r => r.Contains("NO se superó"));
        Assert.Equal(CaseStatus.AwaitingHumanApproval, c.Status);
    }

    [Fact]
    public void A_case_with_pending_data_cannot_be_approved_without_acknowledging_it()
    {
        var c = CaseFixtures.AwaitingApproval(pending: true);

        Assert.Throws<RiskNotAcknowledgedException>(() => c.Approve("Ana"));
    }

    [Fact]
    public void Acknowledging_lets_the_approval_through_and_records_what_was_acknowledged()
    {
        var c = CaseFixtures.AwaitingApproval(auditPasses: false, pending: true);

        c.Approve("Ana", acknowledgeRisks: true);

        Assert.Equal(CaseStatus.Approved, c.Status);
        Assert.Contains("reconociendo expresamente", c.Log[^1]);
        Assert.Contains("NO se superó", c.Log[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Approval_requires_a_reviewer_name(string name)
    {
        Assert.Throws<ArgumentException>(() => CaseFixtures.AwaitingApproval().Approve(name));
    }

    [Fact]
    public void A_decided_case_cannot_be_decided_again()
    {
        var c = CaseFixtures.AwaitingApproval();
        c.Approve("Ana");

        Assert.Throws<InvalidOperationException>(() => c.Approve("Luis"));
        Assert.Throws<InvalidOperationException>(() => c.Reject("Luis", "no"));
    }
}

public class InMemoryRepositoryTests
{
    [Fact]
    public async Task A_saved_case_can_be_read_back()
    {
        var repo = new InMemoryCaseRepository();
        var c = CaseFixtures.AwaitingApproval();

        await repo.SaveAsync(c, default);
        var read = await repo.GetAsync(c.Id, default);

        Assert.NotNull(read);
        Assert.NotSame(c, read);
        Assert.Equal(c.Status, read.Status);
    }

    [Fact]
    public async Task A_missing_case_is_null()
    {
        Assert.Null(await new InMemoryCaseRepository().GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Two_reviewers_cannot_overwrite_each_other()
    {
        var repo = new InMemoryCaseRepository();
        var seed = CaseFixtures.AwaitingApproval();
        await repo.SaveAsync(seed, default);

        var ana = (await repo.GetAsync(seed.Id, default))!;
        var luis = (await repo.GetAsync(seed.Id, default))!;
        ana.Approve("Ana");
        await repo.SaveAsync(ana, default);
        luis.Reject("Luis", "no");

        await Assert.ThrowsAsync<CaseConflictException>(() => repo.SaveAsync(luis, default));
        Assert.Equal(CaseStatus.Approved, (await repo.GetAsync(seed.Id, default))!.Status);
    }

    [Fact]
    public async Task Saving_twice_in_a_row_from_the_same_instance_is_fine()
    {
        var repo = new InMemoryCaseRepository();
        var c = CaseFixtures.AwaitingApproval();
        await repo.SaveAsync(c, default);

        c.Approve("Ana");

        await repo.SaveAsync(c, default);
    }

    [Fact]
    public async Task Listing_filters_by_status_and_returns_the_newest_first()
    {
        var clock = new FakeClock();
        var repo = new InMemoryCaseRepository(clock);
        var first = CaseFixtures.AwaitingApproval();
        var second = CaseFixtures.AwaitingApproval();
        var done = CaseFixtures.AwaitingApproval();
        done.Approve("Ana");

        await repo.SaveAsync(first, default);
        clock.Advance();
        await repo.SaveAsync(second, default);
        clock.Advance();
        await repo.SaveAsync(done, default);

        var pending = await repo.ListSummariesAsync(CaseStatus.AwaitingHumanApproval, default);
        var all = await repo.ListSummariesAsync(null, default);

        Assert.Equal([second.Id, first.Id], pending.Select(s => s.Id));
        Assert.Equal(3, all.Count);
        Assert.Equal(2, (await repo.ListPendingApprovalAsync(default)).Count);
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public void Advance() => _now = _now.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}

public class TerminalStateTests
{
    [Fact]
    public void A_case_decided_by_a_person_can_no_longer_fail()
    {
        var approved = CaseFixtures.AwaitingApproval();
        approved.Approve("Ana");
        var rejected = CaseFixtures.AwaitingApproval();
        rejected.Reject("Ana", "no procede");

        Assert.Throws<InvalidOperationException>(() => approved.Fail("tarde"));
        Assert.Throws<InvalidOperationException>(() => rejected.Fail("tarde"));
        Assert.Equal(CaseStatus.Approved, approved.Status);
    }

    [Fact]
    public void An_open_case_can_still_fail_from_any_working_state()
    {
        var c = new ComplianceCase(CaseFixtures.Change());

        c.Fail("error");

        Assert.Equal(CaseStatus.Failed, c.Status);
    }
}
