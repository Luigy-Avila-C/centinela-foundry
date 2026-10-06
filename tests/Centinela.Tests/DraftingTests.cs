using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class DraftChecksTests
{
    private const string Original = "Pagamos las facturas a 60 días mediante transferencia el día 10 del mes. No comunicamos la fecha de pago a nadie.";

    private static readonly string[] Obligations = ["El destinatario debe informar de la fecha del pago efectivo completo."];

    private static CorrectiveAction Action(
        string proposed, string? quote = "informar de la fecha del pago efectivo completo",
        IReadOnlyList<string>? norm = null, string original = Original) => new(
        "DOC", proposed, "j", null, "P_1", "Pagos", original, norm ?? ["N_1"],
        quote is null ? [] : [new ObligationCoverage(Obligations[0], "cómo", quote)], []);

    private static IReadOnlyList<DraftIssue> Run(CorrectiveAction a, string external = "") =>
        DraftChecks.Run(a, Obligations, ["N_1"], external);

    private const string GoodText =
        "Pagamos las facturas a 60 días mediante transferencia el día 10 del mes. Debemos informar de la fecha del pago efectivo completo a la solución pública.";

    [Fact]
    public void A_good_draft_has_no_blocking_issue()
    {
        Assert.DoesNotContain(Run(Action(GoodText)), i => i.Blocking);
    }

    [Fact]
    public void A_missing_or_unverifiable_coverage_quote_is_reported_per_obligation()
    {
        var missing = Run(Action(GoodText, quote: null));
        var fake = Run(Action(GoodText, quote: "esta frase no está en el borrador"));

        Assert.Contains(missing, i => i.Type == "no_cubre" && i.Obligation == 1);
        Assert.Contains(fake, i => i.Type == "no_cubre" && i.Obligation == 1);
    }

    [Fact]
    public void A_number_not_in_the_original_the_obligations_or_the_norm_is_an_invented_datum()
    {
        var issues = Run(Action(GoodText + " Lo hará en un plazo de 48 horas."));

        Assert.Contains(issues, i => i.Type == "dato_inventado" && i.Quote == "48");
    }

    [Fact]
    public void Numbers_that_come_from_the_original_or_the_norm_are_not_invented()
    {
        // 60 y 10 están en el original; 1028 y 2026 en el título de la norma que se cita.
        var issues = Run(Action(GoodText + " Conforme a la Orden HAC/1028/2026."), external: "Orden HAC/1028/2026, de 2 de octubre");

        Assert.DoesNotContain(issues, i => i.Type == "dato_inventado");
    }

    [Fact]
    public void A_number_inside_a_placeholder_is_not_counted_as_invented()
    {
        var issues = Run(Action(GoodText + " [COMPLETAR: plazo de 30 días que fije la empresa]"));

        Assert.DoesNotContain(issues, i => i.Type == "dato_inventado");
    }

    [Fact]
    public void Rewriting_most_of_the_passage_is_an_unnecessary_change()
    {
        var issues = Run(Action("Debemos informar de la fecha del pago efectivo completo a la solución pública."));

        Assert.Contains(issues, i => i.Type == "cambio_innecesario");
    }

    [Fact]
    public void An_identical_draft_is_reported_as_no_change()
    {
        Assert.Contains(Run(Action(Original)), i => i.Type == "sin_cambios");
    }

    [Theory]
    [InlineData(null)]                   // sin citas
    [InlineData("N_9")]                  // fuera de lo que se le entregó
    public void Norm_citations_must_exist_and_there_must_be_at_least_one(string? cite)
    {
        var issues = Run(Action(GoodText, norm: cite is null ? [] : [cite]));

        Assert.Contains(issues, i => i.Type == "cita_invalida");
    }

    [Fact]
    public void Placeholders_are_reported_but_do_not_block()
    {
        var issues = Run(Action(GoodText + " [COMPLETAR: quién lo hará]"));

        var pending = Assert.Single(issues, i => i.Type == "pendiente");
        Assert.False(pending.Blocking);
        Assert.DoesNotContain(issues, i => i.Blocking);
    }

    [Fact]
    public void An_empty_draft_is_rejected()
    {
        Assert.Contains(DraftChecks.Run(Action(" "), Obligations, ["N_1"], ""), i => i.Blocking);
    }

    [Fact]
    public void Preserved_ratio_counts_repeated_words_once_per_occurrence()
    {
        Assert.Equal(1.0, DraftChecks.PreservedRatio("uno dos tres", "uno dos tres cuatro"), 6);
        Assert.Equal(1.0 / 3, DraftChecks.PreservedRatio("uno dos tres", "uno"), 6);
        // «a a a» en el original solo está dos veces en el borrador: se conservan 2 de 3.
        Assert.Equal(2.0 / 3, DraftChecks.PreservedRatio("a a a", "a a"), 6);
        Assert.Equal(1.0, DraftChecks.PreservedRatio("", "lo que sea"), 6);
    }

    [Fact]
    public void Preserved_ratio_ignores_case_accents_and_punctuation()
    {
        Assert.Equal(1.0, DraftChecks.PreservedRatio("Facturación ELECTRÓNICA, obligatoria.", "facturacion electronica obligatoria"), 6);
    }
}

public class DrafterParsingTests
{
    private static readonly ImpactFinding Finding = new(
        "DOC", "Política", ImpactSeverity.High, "r", "P_1", "2. Emisión", "cita",
        ["N_1"], ["Obligación uno.", "Obligación dos."], null, "Texto original del pasaje.");

    [Fact]
    public void Maps_each_coverage_entry_to_the_obligation_it_numbers_and_carries_the_evidence()
    {
        const string answer = """
            {"texto_propuesto":"Texto nuevo del pasaje.",
             "cobertura":[{"obligacion":2,"como":"c2","cita":"Texto nuevo"},{"obligacion":1,"como":"c1","cita":"del pasaje"}],
             "justificacion":"j","citas_norma":["N_1","N_1"],"pendiente":["dato"]}
            """;

        var a = DrafterAgent.ParseAnswer(answer, Finding);

        Assert.Equal("Texto nuevo del pasaje.", a.ProposedText);
        Assert.Equal("Texto original del pasaje.", a.OriginalText);
        Assert.Equal("P_1", a.PassageId);
        Assert.Equal(["N_1"], a.NormCitations);                                  // sin duplicados
        Assert.Equal(["Obligación dos.", "Obligación uno."], a.Coverage!.Select(c => c.Obligation));
        Assert.Equal(["dato"], a.PendingData);
        Assert.Null(a.Deadline);                                                 // el redactor nunca fija fechas
    }

    [Theory]
    [InlineData("N_009", "N_009")]               // exacta
    [InlineData("N_009.6", "N_009")]             // fragmento + apartado: apunta al mismo fragmento
    [InlineData("N_009 apartado 6", "N_009")]
    [InlineData("N_0091", "N_0091")]             // continúa el identificador: es OTRO, no se toca
    [InlineData("Artículo 7.1 de la Orden", "Artículo 7.1 de la Orden")] // no se adivina
    public void A_citation_that_starts_with_a_valid_id_is_normalized_but_nothing_else_is_guessed(string cited, string expected)
    {
        var result = DrafterAgent.NormalizeCitations([cited], ["N_009", "N_011"]);

        Assert.Equal([expected], result);
    }

    [Fact]
    public void Normalization_prefers_the_longest_matching_id_and_removes_duplicates()
    {
        var result = DrafterAgent.NormalizeCitations(["N_0091.2", "N_009", "N_009.6"], ["N_009", "N_0091"]);

        Assert.Equal(["N_0091", "N_009"], result);
    }

    [Fact]
    public async Task The_drafter_receives_the_exact_list_of_citable_ids()
    {
        var finding = Finding with { NormCitations = ["N_008", "N_009"] };
        var model = new CapturingModel();

        await new DrafterAgent(model, Options.Create(new ModelOptions()))
            .DraftOneAsync(new DraftRequest(finding, [], "Norma", [], null), default);

        Assert.Contains("<identificadores_validos>N_008, N_009</identificadores_validos>", model.Input);
    }

    [Fact]
    public async Task The_drafter_is_told_to_reword_a_shortcoming_and_never_state_a_denied_fact_as_current()
    {
        var model = new CapturingModel();

        await new DrafterAgent(model, Options.Create(new ModelOptions()))
            .DraftOneAsync(new DraftRequest(Finding, [], "Norma", [], null), default);

        Assert.Contains("situación anterior más", model.Instructions);
        Assert.Contains("Nunca afirmes como hecho actual", model.Instructions);
    }

    private sealed class CapturingModel : ILanguageModel
    {
        public string Input { get; private set; } = "";
        public string Instructions { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            Input = input;
            Instructions = instructions;
            return Task.FromResult("""{"texto_propuesto":"x y z","cobertura":[],"citas_norma":["N_008.2"]}""");
        }
    }

    [Fact]
    public void A_coverage_entry_for_a_nonexistent_obligation_is_ignored_so_the_check_reports_it_as_uncovered()
    {
        const string answer = """{"texto_propuesto":"x","cobertura":[{"obligacion":7,"como":"c","cita":"x"}],"citas_norma":[]}""";

        Assert.Empty(DrafterAgent.ParseAnswer(answer, Finding).Coverage!);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"texto_propuesto":"   ","cobertura":[]}""")]
    [InlineData("""{"cobertura":[]}""")]
    public void Unreadable_or_empty_drafts_fail_loudly(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => DrafterAgent.ParseAnswer(answer, Finding));
    }

    [Fact]
    public void Only_findings_with_a_passage_and_obligations_can_be_drafted()
    {
        Assert.True(DrafterAgent.IsActionable(Finding));
        Assert.False(DrafterAgent.IsActionable(Finding with { PassageText = null }));
        Assert.False(DrafterAgent.IsActionable(Finding with { Obligations = [] }));
        Assert.False(DrafterAgent.IsActionable(Finding with { Severity = ImpactSeverity.None }));
    }
}

public class DrafterWorkflowTests
{
    private static ImpactFinding Finding(string id) => new(
        "DOC", "Política", ImpactSeverity.High, "r", id, $"Sección {id}", "cita",
        ["N_1"], ["Obligación."], null, $"Texto original de {id}.");

    private static ComplianceCase CaseWith(params ImpactFinding[] findings)
    {
        var change = new RegulatoryChange(
            "N", "BOE", "Norma de prueba", new Uri("https://www.boe.es/"), new DateOnly(2026, 10, 5), "texto",
            [new TextChunk("N_1", "N", "Norma", "Art. 1", "Texto de la norma.", new DateOnly(2026, 10, 5), new Uri("https://www.boe.es/"))]);

        var c = new ComplianceCase(change);
        c.MarkScreened(true);
        c.SetAnalysis(new ChangeAnalysis("r", [], []));
        c.SetImpact(findings);
        return c;
    }

    private static string Answer(string text) =>
        "{\"texto_propuesto\":\"" + text + "\",\"cobertura\":[{\"obligacion\":1,\"como\":\"c\",\"cita\":\"" + text +
        "\"}],\"justificacion\":\"j\",\"citas_norma\":[\"N_1\"],\"pendiente\":[]}";

    [Fact]
    public async Task First_round_drafts_every_actionable_finding_and_gives_the_model_the_norm_text()
    {
        var model = new RecordingModel();
        var c = CaseWith(Finding("P_1"), Finding("P_2"), Finding("P_1") with { PassageText = null, PassageId = "P_3" });

        var actions = await new DrafterAgent(model, Options.Create(new ModelOptions { Smart = "redactor" })).DraftAsync(c, [], default);

        Assert.Equal(2, actions.Count);                 // el hallazgo sin texto de pasaje no es redactable
        Assert.Equal(2, model.Calls);
        Assert.All(model.Inputs, i => Assert.Contains("Texto de la norma.", i));
        Assert.Equal("redactor", model.Deployments.First());
    }

    [Fact]
    public async Task Later_rounds_only_redraft_passages_the_auditor_objected_to()
    {
        var model = new RecordingModel();
        var drafter = new DrafterAgent(model, Options.Create(new ModelOptions()));
        var c = CaseWith(Finding("P_1"), Finding("P_2"));
        c.SetDraft(await drafter.DraftAsync(c, [], default));
        model.Reset();

        c.ApplyAudit(new AuditVerdict(false, ["[P_2] no_cubre: falta algo"], c.Revision));

        var second = await drafter.DraftAsync(c, ["[P_2] no_cubre: falta algo"], default);

        Assert.Equal(1, model.Calls);                                          // solo P_2
        Assert.Contains("falta algo", model.Inputs.Single());                  // recibe la incidencia
        Assert.Contains("Texto propuesto de", model.Inputs.Single());          // y su borrador anterior
        Assert.Equal(2, second.Count);
        Assert.Same(c.Actions.First(a => a.PassageId == "P_1"), second.First(a => a.PassageId == "P_1")); // P_1 se conserva tal cual
    }

    private sealed class RecordingModel : ILanguageModel
    {
        private readonly List<string> _inputs = [];
        private readonly List<string> _deployments = [];

        public int Calls { get { lock (_inputs) return _inputs.Count; } }
        public IReadOnlyList<string> Inputs { get { lock (_inputs) return _inputs.ToList(); } }
        public IReadOnlyList<string> Deployments { get { lock (_inputs) return _deployments.ToList(); } }
        public void Reset() { lock (_inputs) { _inputs.Clear(); _deployments.Clear(); } }

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            lock (_inputs) { _inputs.Add(input); _deployments.Add(deployment); }
            var id = System.Text.RegularExpressions.Regex.Match(input, @"Texto original de (P_\d)").Groups[1].Value;
            return Task.FromResult(Answer($"Texto propuesto de {id}."));
        }
    }
}

public class AuditorParsingTests
{
    private const string Proposed = "Administración informará a la solución pública del rechazo de la factura.";

    private static string Issue(string type, string? quote, int? obligation = null, string description = "d") =>
        "{\"incidencias\":[{\"tipo\":\"" + type + "\",\"obligacion\":" + (obligation?.ToString() ?? "null") +
        ",\"cita\":" + (quote is null ? "null" : $"\"{quote}\"") + ",\"descripcion\":\"" + description + "\"}]}";

    [Fact]
    public void A_verifiable_issue_blocks_and_is_marked_as_coming_from_the_model()
    {
        var (issues, discarded) = AuditorAgent.ParseAnswer(Issue("dato_inventado", "informará a la solución pública"), Proposed, 1);

        var issue = Assert.Single(issues);
        Assert.True(issue.Blocking);
        Assert.Equal("modelo", issue.Origin);
        Assert.Equal(0, discarded);
    }

    [Fact]
    public void An_issue_whose_quote_is_not_in_the_draft_is_discarded_as_invented()
    {
        var (issues, discarded) = AuditorAgent.ParseAnswer(Issue("contradice_norma", "una frase que el borrador no contiene"), Proposed, 1);

        Assert.Empty(issues);
        Assert.Equal(1, discarded); // una incidencia inventada no puede bloquear un borrador
    }

    [Fact]
    public void Not_covering_an_obligation_needs_no_quote_but_does_need_a_valid_obligation_number()
    {
        Assert.Single(AuditorAgent.ParseAnswer(Issue("no_cubre", null, 1), Proposed, 1).Issues);

        Assert.Empty(AuditorAgent.ParseAnswer(Issue("no_cubre", null, null), Proposed, 1).Issues);   // sin obligación
        Assert.Empty(AuditorAgent.ParseAnswer(Issue("no_cubre", null, 5), Proposed, 1).Issues);      // fuera de rango
    }

    [Fact]
    public void An_ambiguity_is_a_warning_and_does_not_block()
    {
        var (issues, _) = AuditorAgent.ParseAnswer(Issue("ambiguo", null), Proposed, 1);

        Assert.False(Assert.Single(issues).Blocking);
    }

    [Fact]
    public void An_unknown_type_is_discarded()
    {
        var (issues, discarded) = AuditorAgent.ParseAnswer(Issue("quizá_mal", "informará a la solución pública"), Proposed, 1);

        Assert.Empty(issues);
        Assert.Equal(1, discarded);
    }

    [Fact]
    public void No_issues_is_a_valid_answer()
    {
        Assert.Empty(AuditorAgent.ParseAnswer("""{"incidencias":[]}""", Proposed, 1).Issues);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"otra":[]}""")]
    public void Unreadable_answers_fail_loudly(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => AuditorAgent.ParseAnswer(answer, Proposed, 1));
    }
}

public class AuditorDecisionTests
{
    private const string Original = "Pagamos a 60 días. No comunicamos el rechazo de las facturas a nadie.";
    private const string Good = "Pagamos a 60 días. Administración informará del rechazo de las facturas a la solución pública.";

    private static AuditInput Input(string proposed, string? quote = "informará del rechazo de las facturas") => new(
        new CorrectiveAction("DOC", proposed, "j", null, "P_1", "Pagos", Original, ["N_1"],
            quote is null ? [] : [new ObligationCoverage("Debe informar del rechazo.", "c", quote)], []),
        ["Debe informar del rechazo."], ["N_1"],
        [new TextChunk("N_1", "N", "Norma", "Art. 1", "Texto", new DateOnly(2026, 1, 1), new Uri("https://www.boe.es/"))],
        "Norma de prueba");

    private static AuditorAgent Auditor(string modelAnswer) =>
        new(new FixedModel(modelAnswer), Options.Create(new ModelOptions { Judge = "juez" }));

    [Fact]
    public async Task A_clean_draft_with_no_model_objections_passes()
    {
        var r = await Auditor("""{"incidencias":[]}""").AuditOneAsync(Input(Good), default);

        Assert.True(r.Passed);
    }

    [Fact]
    public async Task The_code_rejects_a_draft_even_when_the_model_finds_nothing_wrong()
    {
        // El borrador inventa «48»: el modelo no lo ve, pero la comprobación automática no se puede convencer.
        var r = await Auditor("""{"incidencias":[]}""").AuditOneAsync(Input(Good + " Lo hará en 48 horas."), default);

        Assert.False(r.Passed);
        Assert.Contains(r.Issues, i => i.Type == "dato_inventado" && i.Origin == "automatica");
    }

    [Fact]
    public async Task The_model_can_reject_what_the_code_cannot_see()
    {
        var model = """{"incidencias":[{"tipo":"no_cubre","obligacion":1,"cita":null,"descripcion":"Solo promete revisarlo."}]}""";

        var r = await Auditor(model).AuditOneAsync(Input(Good), default);

        Assert.False(r.Passed);
        Assert.Contains(r.Issues, i => i.Type == "no_cubre" && i.Origin == "modelo");
    }

    [Fact]
    public async Task A_non_verifiable_model_issue_is_discarded_and_counted_instead_of_blocking()
    {
        var model = """{"incidencias":[{"tipo":"dato_inventado","obligacion":null,"cita":"frase inexistente","descripcion":"x"}]}""";

        var r = await Auditor(model).AuditOneAsync(Input(Good), default);

        Assert.True(r.Passed);
        Assert.Equal(1, r.DiscardedModelIssues);
    }

    [Fact]
    public async Task Placeholders_do_not_make_a_draft_fail()
    {
        var r = await Auditor("""{"incidencias":[]}""")
            .AuditOneAsync(Input(Good + " [COMPLETAR: responsable de la comunicación]"), default);

        Assert.True(r.Passed);
        Assert.Contains(r.Issues, i => i.Type == "pendiente" && !i.Blocking);
    }

    [Fact]
    public async Task The_auditor_uses_the_judge_model_not_the_drafters()
    {
        var model = new FixedModel("""{"incidencias":[]}""");

        await new AuditorAgent(model, Options.Create(new ModelOptions { Smart = "redactor", Judge = "juez" }))
            .AuditOneAsync(Input(Good), default);

        Assert.Equal("juez", model.Deployment);
    }

    [Fact]
    public async Task Case_level_audit_with_no_drafts_never_passes()
    {
        var c = new ComplianceCase(new RegulatoryChange(
            "N", "BOE", "T", new Uri("https://www.boe.es/"), new DateOnly(2026, 10, 5), "x"));
        c.MarkScreened(true);
        c.SetAnalysis(new ChangeAnalysis("r", [], []));
        c.SetImpact([new ImpactFinding("D", "D", ImpactSeverity.High, "r")]); // sin pasaje: no es redactable
        c.SetDraft([]);

        var verdict = await Auditor("""{"incidencias":[]}""").AuditAsync(c, default);

        Assert.False(verdict.Passed); // una ausencia no se da por buena: se escala a una persona
    }

    private sealed class FixedModel(string answer) : ILanguageModel
    {
        public string Deployment { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            Deployment = deployment;
            return Task.FromResult(answer);
        }
    }
}

public class ShippedAuditorDatasetTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "evaluaciones", "auditor-borradores.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new FileNotFoundException("No se encuentra el repositorio.");
    }

    private static IReadOnlyList<AuditDraftCase> Cases() =>
        AuditorEvaluation.LoadDataset(File.ReadAllText(Path.Combine(Repo(), "evaluaciones", "auditor-borradores.json")));

    private static Dictionary<(string Doc, string Section), TextChunk> Passages() =>
        Directory.GetFiles(Path.Combine(Repo(), "datos", "empresa-ejemplo"), "*.md")
            .SelectMany(f => DocumentChunker.Chunk(CompanyDocumentLoader.Parse(
                File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), new Uri(f), new DateOnly(2026, 1, 1))))
            .ToDictionary(c => (c.DocumentId, c.Label));

    [Fact]
    public void Every_finding_points_to_a_real_passage_and_they_are_exactly_the_seven_labelled_as_affected()
    {
        var passages = Passages();
        var findings = Cases().Select(c => c.Finding).DistinctBy(f => f.Id).ToList();

        Assert.All(findings, f => Assert.True(passages.ContainsKey((f.Document, f.Section)), $"{f.Document} · {f.Section}"));

        var impact = ImpactEvaluation.LoadDataset(File.ReadAllText(Path.Combine(Repo(), "evaluaciones", "impacto-aurora.json")))
            .Where(c => c.Expected == ImpactExpectation.Affected)
            .Select(c => (c.DocumentId, c.Section)).Order().ToList();

        Assert.Equal(impact, findings.Select(f => (f.Document, f.Section)).Order().ToList());
    }

    [Fact]
    public void The_splits_use_different_findings_and_have_the_announced_counts()
    {
        var cases = Cases();

        Assert.Equal(4, cases.Count(c => c.Finding.Split == "dev" && c.ShouldApprove));
        Assert.Equal(8, cases.Count(c => c.Finding.Split == "dev" && !c.ShouldApprove));
        Assert.Equal(3, cases.Count(c => c.Finding.Split == "test" && c.ShouldApprove));
        Assert.Equal(6, cases.Count(c => c.Finding.Split == "test" && !c.ShouldApprove));

        // Cada hallazgo tiene un borrador bueno y dos defectuosos.
        Assert.All(cases.GroupBy(c => c.Finding.Id), g =>
        {
            Assert.Equal(1, g.Count(c => c.ShouldApprove));
            Assert.Equal(2, g.Count(c => !c.ShouldApprove));
        });
    }

    [Fact]
    public void Every_coverage_quote_really_appears_in_its_draft()
    {
        // Si no, la comprobación automática de cobertura rechazaría el borrador por un fallo del propio caso de prueba, no por su contenido.
        foreach (var c in Cases())
        {
            Assert.All(c.Coverage, cov => Assert.True(
                QuoteMatch.Appears(cov.Quote, c.Text), $"{c.Id}: «{cov.Quote}»"));
        }
    }

    [Fact]
    public void The_good_drafts_pass_every_automatic_check()
    {
        var passages = Passages();

        foreach (var c in Cases().Where(c => c.ShouldApprove))
        {
            var original = passages[(c.Finding.Document, c.Finding.Section)].Text;
            var action = AuditorEvaluation.ToAction(c, "P", original);

            var blocking = DraftChecks.Run(action, c.Finding.Obligations, c.Finding.NormIds, "").Where(i => i.Blocking).ToList();

            Assert.True(blocking.Count == 0, $"{c.Id}: {string.Join("; ", blocking.Select(i => i.Type + " " + i.Description))}");
        }
    }

    [Fact]
    public void Documents_which_flawed_drafts_the_code_alone_can_catch_and_which_need_the_model()
    {
        var passages = Passages();
        var byCode = new List<string>();
        var needModel = new List<string>();

        foreach (var c in Cases().Where(c => !c.ShouldApprove))
        {
            var original = passages[(c.Finding.Document, c.Finding.Section)].Text;
            var blocking = DraftChecks.Run(AuditorEvaluation.ToAction(c, "P", original), c.Finding.Obligations, c.Finding.NormIds, "")
                .Any(i => i.Blocking);

            (blocking ? byCode : needModel).Add(c.Id);
        }

        // Cifras inventadas y reescrituras completas las atrapa el código; no cubrir de verdad o contradecir la
        // norma solo puede juzgarlo el modelo. Si este reparto cambia, la medición del auditor significa otra cosa.
        Assert.Equal(["b03", "b06", "b09", "b12", "b14", "b20", "b21"], byCode.Order());
        Assert.Equal(["b02", "b05", "b08", "b11", "b15", "b17", "b18"], needModel.Order());
    }
}

public class AuditEvalReportTests
{
    private static AuditDraftCase Case(string id, bool approve, string? defect = null) => new(
        id, new AuditFinding("H", "dev", "d", "s", [], []), approve, defect, "t", [], [], [], "");

    private static AuditResult Result(bool passed, params DraftIssue[] issues) => new(passed, issues, 0);

    [Fact]
    public void Separates_what_is_caught_by_code_by_the_model_or_both_and_lists_failures_by_case()
    {
        var report = AuditorEvaluation.Evaluate([
            (Case("g1", true), Result(true)),                                                                      // bien aprobado
            (Case("g2", true), Result(false, new DraftIssue("ambiguo_grave", "x", Origin: "modelo"))),             // falsa alarma
            (Case("f1", false, "dato_inventado"), Result(false, new DraftIssue("dato_inventado", "x"))),           // código
            (Case("f2", false, "no_cubre"), Result(false, new DraftIssue("no_cubre", "x", Origin: "modelo"))),     // modelo
            (Case("f3", false, "no_cubre"), Result(false,
                new DraftIssue("no_cubre", "x"), new DraftIssue("no_cubre", "y", Origin: "modelo"))),              // ambos
            (Case("f4", false, "contradice_norma"), Result(true)),                                                 // se escapó
        ]);

        Assert.Equal((2, 1), (report.GoodTotal, report.GoodApproved));
        Assert.Equal((4, 3), (report.FlawedTotal, report.FlawedRejected));
        Assert.Equal(3.0 / 4, report.CatchRate, 6);
        Assert.Equal(1.0 / 2, report.ApprovalRate, 6);
        Assert.Equal((1, 1, 1), (report.CaughtByAutomaticOnly, report.CaughtByModelOnly, report.CaughtByBoth));
        Assert.Equal(3, report.DefectTypeMatched);
        Assert.Single(report.FalseAlarms);
        Assert.Equal(["f4 (contradice_norma)"], report.Missed);
    }

    [Fact]
    public void A_non_blocking_issue_does_not_count_as_catching_a_flawed_draft()
    {
        var report = AuditorEvaluation.Evaluate([
            (Case("f1", false, "no_cubre"), Result(true, new DraftIssue("pendiente", "x", Blocking: false))),
        ]);

        Assert.Equal(0, report.FlawedRejected);
    }

    [Fact]
    public void Metrics_with_no_denominator_are_not_a_number()
    {
        var report = AuditorEvaluation.Evaluate([(Case("g1", true), Result(true))]);

        Assert.True(double.IsNaN(report.CatchRate));
    }
}
