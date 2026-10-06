using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class CompanyDocumentLoaderTests
{
    private const string Markdown = """
        # Política de ejemplo

        > DOCUMENTO FICTICIO. Aviso que no es contenido.

        ## 1. Objeto

        Primera línea
        continúa en la segunda.

        ## 2. Plazos

        Treinta días.

        Otro párrafo.
        """;

    [Fact]
    public void Each_second_level_heading_opens_a_section_and_notes_are_not_content()
    {
        var doc = CompanyDocumentLoader.Parse(Markdown, "00-ejemplo", new Uri("file:///x.md"), new DateOnly(2026, 1, 1));

        Assert.Equal("Política de ejemplo", doc.Title);

        var chunks = DocumentChunker.Chunk(doc);
        Assert.Equal(["1. Objeto", "2. Plazos"], chunks.Select(c => c.Label));
        Assert.DoesNotContain(chunks, c => c.Text.Contains("FICTICIO"));
        Assert.Contains("Primera línea continúa en la segunda.", chunks[0].Text); // las líneas de un párrafo se unen
        Assert.Contains("Otro párrafo.", chunks[1].Text);
    }

    [Fact]
    public void Chunk_ids_are_valid_search_keys()
    {
        var doc = CompanyDocumentLoader.Parse(Markdown, "01-politica-de-facturacion", new Uri("file:///x.md"), new DateOnly(2026, 1, 1));

        Assert.All(DocumentChunker.Chunk(doc), c => Assert.Matches("^[A-Za-z0-9_=-]+$", c.Id));
    }
}

public class ImpactParsingTests
{
    private static readonly TextChunk Passage = new(
        "DOC_001", "DOC", "Política", "2. Emisión", "Las facturas se envían por correo en PDF y no se remite copia a ninguna plataforma.",
        new DateOnly(2026, 1, 1), new Uri("file:///x"));

    private static readonly Claim[] Shown =
    [
        new("Deben remitir una copia fiel UBL.", ["NORMA_009"]),
        new("Las facturas deben ser UBL.", ["NORMA_007"]),
    ];

    // La exigencia debe ser un fragmento literal de la obligación n (con ≥ 8 caracteres).
    private static string Requirement(int n) => n == 1 ? "copia fiel UBL" : n == 2 ? "deben ser UBL" : "inexistente";

    private static string Finding(
        int n, string effect, string severity, string quote,
        string subject = "empresa", string kind = "deber", string basis = "afirma", string? requirement = null) =>
        "{\"hallazgos\":[{\"obligacion\":" + n + ",\"exigencia\":\"" + (requirement ?? Requirement(n)) + "\",\"sujeto\":\"" + subject +
        "\",\"tipo\":\"" + kind + "\",\"base\":\"" + basis + "\",\"efecto\":\"" + effect + "\",\"gravedad\":\"" + severity +
        "\",\"cita\":\"" + quote + "\",\"motivo\":\"m\",\"accion\":\"a\"}]}";

    [Fact]
    public void A_duty_of_a_third_party_the_company_depends_on_still_counts()
    {
        var (f, unverified) = ImpactAgent.ParseAnswer(
            Finding(1, "falta_requisito", "media", "no se remite copia a ninguna plataforma", subject: "tercero_del_que_depende"), Passage, Shown);

        Assert.NotNull(f);
        Assert.Equal(0, unverified);
    }

    [Theory]
    [InlineData("otro", "deber", "afirma")]          // obligación de un sujeto que la empresa no usa
    [InlineData("empresa", "facultad", "afirma")]    // «podrá…» no es un deber
    [InlineData("empresa", "condicion", "afirma")]   // «se entenderá cumplida cuando…» no es un deber
    [InlineData("empresa", "deber", "silencio")]     // «no se menciona…» no es un incumplimiento
    public void Only_a_duty_of_the_company_shown_by_something_the_passage_affirms_becomes_a_finding(string subject, string kind, string basis)
    {
        var outcome = ImpactAgent.ParseOutcome(
            Finding(1, "incumple", "alta", "no se remite copia a ninguna plataforma", subject, kind, basis), Passage, Shown);

        // Es una respuesta bien formada y verificable, pero no cumple las reglas: se descarta por regla, no por cita.
        Assert.Null(outcome.Finding);
        Assert.Equal(1, outcome.RejectedByRule);
        Assert.Equal(0, outcome.Unverified);
    }

    [Fact]
    public void A_requirement_that_is_not_literally_in_the_obligation_is_unverifiable()
    {
        // El juez «cita» una exigencia que la obligación no contiene: no se puede comprobar que la haya leído.
        var outcome = ImpactAgent.ParseOutcome(
            Finding(1, "incumple", "alta", "no se remite copia a ninguna plataforma", requirement: "plazo de veinte días hábiles"), Passage, Shown);

        Assert.Null(outcome.Finding);
        Assert.Equal(1, outcome.Unverified);
    }

    [Fact]
    public void A_finding_that_omits_the_new_mandatory_fields_is_unverifiable()
    {
        const string old = """{"hallazgos":[{"obligacion":1,"efecto":"incumple","gravedad":"alta","cita":"no se remite copia a ninguna plataforma"}]}""";

        var outcome = ImpactAgent.ParseOutcome(old, Passage, Shown);

        Assert.Null(outcome.Finding);
        Assert.Equal(1, outcome.Unverified);
    }

    [Fact]
    public void A_verified_finding_carries_its_evidence_and_the_norm_citations()
    {
        var (f, unverified) = ImpactAgent.ParseAnswer(
            Finding(1, "falta_requisito", "alta", "no se remite copia a ninguna plataforma"), Passage, Shown);

        Assert.NotNull(f);
        Assert.Equal(0, unverified);
        Assert.Equal(ImpactSeverity.High, f!.Severity);
        Assert.Equal("DOC_001", f.PassageId);
        Assert.Equal("2. Emisión", f.PassageLabel);
        Assert.Equal(["NORMA_009"], f.NormCitations);
        Assert.Equal(["Deben remitir una copia fiel UBL."], f.Obligations);
        Assert.Equal("a", f.SuggestedAction);
    }

    [Fact]
    public void A_finding_whose_quote_is_not_in_the_passage_is_discarded_as_invented()
    {
        // El juez «ve» un problema que el pasaje no dice: sin cita literal verificable, no hay hallazgo.
        var (f, unverified) = ImpactAgent.ParseAnswer(
            Finding(1, "incumple", "alta", "las facturas se envían en papel certificado"), Passage, Shown);

        Assert.Null(f);
        Assert.Equal(1, unverified);
    }

    [Theory]
    [InlineData(0, "incumple", "alta")]        // la numeración empieza en 1
    [InlineData(3, "incumple", "alta")]        // solo se mostraron 2 obligaciones
    [InlineData(1, "quizá", "alta")]           // efecto fuera del vocabulario
    [InlineData(1, "incumple", "crítica")]     // gravedad fuera del vocabulario
    public void Findings_with_an_invalid_obligation_effect_or_severity_are_discarded(int n, string effect, string severity)
    {
        var (f, unverified) = ImpactAgent.ParseAnswer(
            Finding(n, effect, severity, "no se remite copia a ninguna plataforma"), Passage, Shown);

        Assert.Null(f);
        Assert.Equal(1, unverified);
    }

    [Fact]
    public void No_findings_is_a_valid_answer_and_not_a_failure()
    {
        var (f, unverified) = ImpactAgent.ParseAnswer("""{"hallazgos":[]}""", Passage, Shown);

        Assert.Null(f);
        Assert.Equal(0, unverified);
    }

    [Fact]
    public void Several_findings_on_one_passage_become_one_with_the_highest_severity()
    {
        const string answer = """
            {"hallazgos":[
              {"obligacion":2,"exigencia":"deben ser UBL","sujeto":"empresa","tipo":"deber","base":"afirma","efecto":"incumple","gravedad":"baja","cita":"se envían por correo en PDF","motivo":"uno","accion":"x"},
              {"obligacion":1,"exigencia":"copia fiel UBL","sujeto":"empresa","tipo":"deber","base":"afirma","efecto":"falta_requisito","gravedad":"alta","cita":"no se remite copia a ninguna plataforma","motivo":"dos","accion":"y"}]}
            """;

        var (f, _) = ImpactAgent.ParseAnswer(answer, Passage, Shown);

        Assert.Equal(ImpactSeverity.High, f!.Severity);
        Assert.Equal("no se remite copia a ninguna plataforma", f.PassageQuote); // la cita del hallazgo más grave
        Assert.Equal(["NORMA_007", "NORMA_009"], f.NormCitations!.Order());
        Assert.Contains("uno", f.Rationale);
        Assert.Contains("dos", f.Rationale);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"otra":[]}""")]
    public void Unreadable_answers_fail_loudly(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => ImpactAgent.ParseAnswer(answer, Passage, Shown));
    }
}

public class ImpactAgentTests
{
    private static readonly TextChunk PdfPassage = Passage("P_001", "Emisión", "Las facturas se envían en PDF por correo.");
    private static readonly TextChunk OtherPassage = Passage("P_002", "Jurisdicción", "Las partes se someten a los tribunales de la ciudad.");

    private static TextChunk Passage(string id, string label, string text) =>
        new(id, "DOC", "Documento", label, text, new DateOnly(2026, 1, 1), new Uri("file:///x"));

    // Cada obligación contiene la frase «debe remitir una copia fiel UBL», que el juez falso cita como exigencia.
    private static ChangeAnalysis Analysis(params string[] claimTexts) =>
        new("r", [], [], claimTexts.Select((t, i) => new Claim($"{t}: la empresa debe remitir una copia fiel UBL", [$"NORMA_{i + 1:000}"])).ToList());

    [Fact]
    public async Task Each_retrieved_passage_is_judged_once_even_if_many_obligations_point_to_it()
    {
        var model = new PdfJudge();
        var index = new ScriptedIndex(PdfPassage); // todas las obligaciones recuperan el mismo pasaje
        var agent = Build(model, index);

        var result = await agent.AssessDetailedAsync(Analysis("a", "b", "c", "d"), default);

        Assert.Equal(1, model.Calls);
        Assert.Equal(1, result.PassagesJudged);
        Assert.Equal(["P_001"], result.CandidatePassageIds);
        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task Passages_without_a_problem_produce_no_finding_and_unretrieved_passages_are_never_judged()
    {
        var model = new PdfJudge();
        var agent = Build(model, new ScriptedIndex(OtherPassage));

        var result = await agent.AssessDetailedAsync(Analysis("a", "b"), default);

        Assert.Empty(result.Findings);
        Assert.Equal(1, model.Calls); // se juzgó el pasaje recuperado; el otro ni se miró
        Assert.DoesNotContain("P_001", result.CandidatePassageIds);
    }

    [Fact]
    public async Task Only_the_closest_obligations_are_shown_to_the_judge_for_each_passage()
    {
        var model = new PdfJudge();
        var agent = Build(model, new ScriptedIndex(PdfPassage), new ImpactOptions { MaxClaimsPerPassage = 3 });

        await agent.AssessDetailedAsync(Analysis(Enumerable.Range(1, 10).Select(i => $"obligación {i}").ToArray()), default);

        // Con 10 obligaciones apuntando al pasaje, solo se le enseñan 3: el prompt no crece sin control.
        Assert.Equal(3, model.LastInput.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")));
    }

    [Fact]
    public async Task An_analysis_without_claims_cannot_be_assessed()
    {
        var agent = Build(new PdfJudge(), new ScriptedIndex(PdfPassage));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AssessDetailedAsync(new ChangeAnalysis("sin afirmaciones", [], []), default));
    }

    [Fact]
    public async Task Retries_an_unreadable_judgement_once_then_gives_up_loudly()
    {
        var flaky = new PdfJudge(failFirst: true);
        var ok = await Build(flaky, new ScriptedIndex(PdfPassage)).AssessDetailedAsync(Analysis("a"), default);
        Assert.Single(ok.Findings);
        Assert.Equal(2, flaky.Calls);

        var broken = new PdfJudge(failAlways: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build(broken, new ScriptedIndex(PdfPassage)).AssessDetailedAsync(Analysis("a"), default));
        Assert.Equal(2, broken.Calls);
    }

    [Fact]
    public async Task Uses_the_configured_deployment_so_two_judges_can_be_compared()
    {
        var model = new PdfJudge();
        var agent = Build(model, new ScriptedIndex(PdfPassage), new ImpactOptions { Deployment = "gpt-5.1" });

        await agent.AssessDetailedAsync(Analysis("a"), default);

        Assert.Equal("gpt-5.1", model.Deployment);
    }

    // ───── montaje ─────

    private static ImpactAgent Build(PdfJudge model, ScriptedIndex index, ImpactOptions? options = null) =>
        new(model, new Embeddings(), index, Options.Create(new ModelOptions { Smart = "smart" }),
            Options.Create(options ?? new ImpactOptions()));

    /// <summary>Juez que ve un problema en cualquier pasaje que hable de PDF, citándolo literalmente.</summary>
    private sealed class PdfJudge(bool failFirst = false, bool failAlways = false) : ILanguageModel
    {
        private int _calls;
        public int Calls => _calls;
        public string Deployment { get; private set; } = "";
        public string LastInput { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            (Deployment, LastInput) = (deployment, input);

            if (failAlways || (failFirst && call == 1)) return Task.FromResult("no es json");

            return Task.FromResult(input.Contains("PDF")
                ? """{"hallazgos":[{"obligacion":1,"exigencia":"debe remitir una copia fiel UBL","sujeto":"empresa","tipo":"deber","base":"afirma","efecto":"falta_requisito","gravedad":"alta","cita":"se envían en PDF por correo","motivo":"m","accion":"a"}]}"""
                : """{"hallazgos":[]}""");
        }
    }

    private sealed class ScriptedIndex(TextChunk always) : IChunkIndex
    {
        public Task EnsureCreatedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<NormHit>> SearchAsync(string text, float[] vector, int top, string? exclude, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NormHit>>([new NormHit(always, 1.0 - text.Length * 0.0001)]);
    }

    private sealed class Embeddings : IEmbeddingModel
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f }).ToList());
    }
}

public class ImpactEvaluationTests
{
    private static TextChunk Chunk(string id, string doc, string label) =>
        new(id, doc, doc, label, "texto", new DateOnly(2026, 1, 1), new Uri("file:///x"));

    private static readonly TextChunk[] Chunks =
    [
        Chunk("A_1", "A", "uno"), Chunk("A_2", "A", "dos"), Chunk("A_3", "A", "tres"), Chunk("A_4", "A", "cuatro"), Chunk("A_5", "A", "cinco"),
    ];

    private static ImpactCase Case(string section, ImpactExpectation e, ImpactSeverity? s = null) =>
        new("A", section, "dev", e, s, [], "");

    private static ImpactFinding Found(string passageId, ImpactSeverity s) =>
        new("A", "A", s, "m", PassageId: passageId);

    [Fact]
    public void Counts_each_kind_of_hit_and_miss_and_separates_retrieval_from_judgement()
    {
        ImpactCase[] cases =
        [
            Case("uno", ImpactExpectation.Affected, ImpactSeverity.High),      // encontrado (acierto)
            Case("dos", ImpactExpectation.Affected, ImpactSeverity.Medium),    // llegó al juez y no lo marcó (fallo de juicio)
            Case("tres", ImpactExpectation.Affected, ImpactSeverity.Medium),   // ni siquiera se recuperó (fallo de búsqueda)
            Case("cuatro", ImpactExpectation.NotAffected),                     // marcado por error
            Case("cinco", ImpactExpectation.NotAffected),                      // bien no marcado
        ];

        var assessment = new ImpactAssessment(
            [Found("A_1", ImpactSeverity.High), Found("A_4", ImpactSeverity.Low)],
            ["A_1", "A_2", "A_4", "A_5"], 4, 0);

        var r = ImpactEvaluation.Evaluate(cases, Chunks, assessment);

        Assert.Equal((1, 1, 2, 1), (r.TruePositives, r.FalsePositives, r.FalseNegatives, r.TrueNegatives));
        Assert.Equal(1.0 / 2, r.Precision, 6);
        Assert.Equal(1.0 / 3, r.Recall, 6);
        Assert.Equal(2.0 / 3, r.RetrievalRecall, 6);
        Assert.Equal(["A · tres"], r.NotRetrievedCases);
        Assert.Equal(["A · cuatro"], r.FalsePositiveCases);
    }

    [Fact]
    public void Doubtful_cases_are_reported_but_never_count_for_or_against()
    {
        var r = ImpactEvaluation.Evaluate(
            [Case("uno", ImpactExpectation.Doubtful), Case("dos", ImpactExpectation.Doubtful)], Chunks,
            new ImpactAssessment([Found("A_1", ImpactSeverity.Low)], ["A_1"], 1, 0));

        Assert.Equal(2, r.Doubtful);
        Assert.Equal(1, r.DoubtfulFlagged);
        Assert.Equal((0, 0, 0, 0), (r.TruePositives, r.FalsePositives, r.FalseNegatives, r.TrueNegatives));
    }

    [Fact]
    public void Severity_is_compared_exactly_and_within_one_level()
    {
        ImpactCase[] cases =
        [
            Case("uno", ImpactExpectation.Affected, ImpactSeverity.High),
            Case("dos", ImpactExpectation.Affected, ImpactSeverity.High),
            Case("tres", ImpactExpectation.Affected, ImpactSeverity.High),
        ];

        var r = ImpactEvaluation.Evaluate(cases, Chunks, new ImpactAssessment(
            [Found("A_1", ImpactSeverity.High), Found("A_2", ImpactSeverity.Medium), Found("A_3", ImpactSeverity.Low)],
            ["A_1", "A_2", "A_3"], 3, 0));

        Assert.Equal((3, 1, 2), (r.SeverityComparable, r.SeverityExact, r.SeverityWithinOne));
    }

    [Fact]
    public void A_label_that_matches_no_real_passage_fails_loudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ImpactEvaluation.Evaluate(
            [Case("no existe", ImpactExpectation.NotAffected)], Chunks, new ImpactAssessment([], [], 0, 0)));

        Assert.Contains("no existe", ex.Message);
    }

    [Fact]
    public void Metrics_with_no_denominator_are_not_a_number_rather_than_zero()
    {
        var r = ImpactEvaluation.Evaluate(
            [Case("uno", ImpactExpectation.NotAffected)], Chunks, new ImpactAssessment([], [], 0, 0));

        Assert.True(double.IsNaN(r.Precision));
        Assert.True(double.IsNaN(r.Recall));
    }
}

public class ShippedImpactDatasetTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "evaluaciones", "impacto-aurora.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new FileNotFoundException("No se encuentra el repositorio.");
    }

    private static (IReadOnlyList<ImpactCase> Cases, IReadOnlyList<TextChunk> Chunks) Load()
    {
        var repo = Repo();
        var cases = ImpactEvaluation.LoadDataset(File.ReadAllText(Path.Combine(repo, "evaluaciones", "impacto-aurora.json")));

        var chunks = Directory.GetFiles(Path.Combine(repo, "datos", "empresa-ejemplo"), "*.md").Order()
            .SelectMany(f => DocumentChunker.Chunk(CompanyDocumentLoader.Parse(
                File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), new Uri(f), new DateOnly(2026, 1, 1))))
            .ToList();

        return (cases, chunks);
    }

    [Fact]
    public void Every_label_points_to_a_real_passage_and_every_passage_has_a_label()
    {
        var (cases, chunks) = Load();

        // Una etiqueta sin pasaje mediría otra cosa sin avisar; un pasaje sin etiqueta quedaría fuera de la medición.
        foreach (var c in cases)
        {
            Assert.Contains(chunks, x => x.DocumentId == c.DocumentId && x.Label == c.Section);
        }

        foreach (var chunk in chunks)
        {
            Assert.Contains(cases, c => c.DocumentId == chunk.DocumentId && c.Section == chunk.Label);
        }

        Assert.Equal(chunks.Count, cases.Count);
    }

    [Fact]
    public void The_two_splits_use_different_documents_and_both_contain_positives_and_negatives()
    {
        var (cases, _) = Load();

        var devDocs = cases.Where(c => c.Split == "dev").Select(c => c.DocumentId).ToHashSet();
        var testDocs = cases.Where(c => c.Split == "test").Select(c => c.DocumentId).ToHashSet();

        Assert.Empty(devDocs.Intersect(testDocs)); // si no, ajustar con dev contaminaría test

        foreach (var split in new[] { "dev", "test" })
        {
            Assert.Contains(cases, c => c.Split == split && c.Expected == ImpactExpectation.Affected);
            Assert.Contains(cases, c => c.Split == split && c.Expected == ImpactExpectation.NotAffected);
        }
    }

    [Fact]
    public void The_labels_have_the_counts_announced_in_the_dataset()
    {
        var (cases, _) = Load();

        Assert.Equal(4, cases.Count(c => c.Split == "dev" && c.Expected == ImpactExpectation.Affected));
        Assert.Equal(3, cases.Count(c => c.Split == "test" && c.Expected == ImpactExpectation.Affected));
        Assert.Equal(1, cases.Count(c => c.Expected == ImpactExpectation.Doubtful));
    }

    [Fact]
    public void Every_affected_case_names_the_norm_facts_that_justify_it()
    {
        var (cases, _) = Load();

        Assert.All(cases.Where(c => c.Expected == ImpactExpectation.Affected), c => Assert.NotEmpty(c.Facts));
    }
}
