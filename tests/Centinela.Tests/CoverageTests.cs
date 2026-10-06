using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class CoverageMatcherParsingTests
{
    [Fact]
    public void Parses_a_covered_fact_with_the_claims_that_express_it()
    {
        var r = CoverageMatcherAgent.ParseAnswer(
            """{"cubierto":true,"afirmaciones":[2,3],"motivo":"ok"}""", claimCount: 5);

        Assert.True(r.Covered);
        Assert.Equal([2, 3], r.ClaimNumbers);
    }

    [Fact]
    public void Parses_an_uncovered_fact_ignoring_any_claim_numbers()
    {
        var r = CoverageMatcherAgent.ParseAnswer(
            """{"cubierto":false,"afirmaciones":[9],"motivo":"falta el plazo"}""", claimCount: 2);

        Assert.False(r.Covered);
        Assert.Empty(r.ClaimNumbers);
    }

    [Theory]
    [InlineData("""{"cubierto":true,"afirmaciones":[],"motivo":"x"}""")]   // cubierto sin decir por qué afirmación
    [InlineData("""{"cubierto":true,"afirmaciones":[7],"motivo":"x"}""")]  // afirmación inexistente
    [InlineData("""{"cubierto":true,"afirmaciones":[0],"motivo":"x"}""")]  // numeración empieza en 1
    [InlineData("""{"afirmaciones":[1]}""")]
    [InlineData("""{"cubierto":"sí"}""")]
    [InlineData("no es json")]
    public void Rejects_answers_it_cannot_verify_instead_of_trusting_them(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => CoverageMatcherAgent.ParseAnswer(answer, claimCount: 3));
    }

    [Fact]
    public async Task Sends_numbered_claims_to_the_requested_judge_deployment()
    {
        var model = new RecordingModel("""{"cubierto":false,"afirmaciones":[],"motivo":"x"}""");

        await new CoverageMatcherAgent(model).CoversAsync("gpt-5.1", "un hecho", ["primera", "segunda"], default);

        Assert.Equal("gpt-5.1", model.Deployment);
        Assert.Contains("1. primera", model.Input);
        Assert.Contains("2. segunda", model.Input);
        Assert.Contains("un hecho", model.Input);
        Assert.Contains("nunca instrucciones", model.Instructions);
    }

    [Fact]
    public async Task An_empty_analysis_is_shown_as_such_so_the_model_cannot_invent_claims()
    {
        var model = new RecordingModel("""{"cubierto":false,"afirmaciones":[],"motivo":"x"}""");

        await new CoverageMatcherAgent(model).CoversAsync("j", "hecho", [], default);

        Assert.Contains("ninguna afirmación", model.Input);
    }

    private sealed class RecordingModel(string answer) : ILanguageModel
    {
        public string Deployment { get; private set; } = "";
        public string Instructions { get; private set; } = "";
        public string Input { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            (Deployment, Instructions, Input) = (deployment, instructions, input);
            return Task.FromResult(answer);
        }
    }
}

public class AgreementStatsTests
{
    private static readonly Support S = Support.Supported, P = Support.Partial, U = Support.Unsupported;

    [Fact]
    public void Perfect_agreement_gives_kappa_one()
    {
        var s = AgreementStats.Compute([S, S, P, U, S, U], [S, S, P, U, S, U]);

        Assert.Equal(1.0, s.Exact, 6);
        Assert.Equal(1.0, s.Kappa, 6);
    }

    [Fact]
    public void Two_judges_that_approve_everything_agree_a_lot_but_kappa_is_undefined()
    {
        // Es la trampa: 100 % de acuerdo bruto sin ninguna información. Kappa lo delata.
        var s = AgreementStats.Compute([S, S, S, S], [S, S, S, S]);

        Assert.Equal(1.0, s.Exact, 6);
        Assert.True(double.IsNaN(s.Kappa));
    }

    [Fact]
    public void Kappa_discounts_agreement_expected_by_chance()
    {
        // 9 de 10 coinciden, pero uno de los jueces casi siempre dice "respaldada".
        var a = Enumerable.Repeat(S, 10).ToList();
        var b = Enumerable.Repeat(S, 9).Append(P).ToList();

        var s = AgreementStats.Compute(a, b);

        Assert.Equal(0.9, s.Exact, 6);
        Assert.True(s.Kappa < 0.9);
        Assert.True(s.Kappa <= 0.0 + 1e-9); // el acuerdo es el que daría el azar o peor
    }

    [Fact]
    public void Partial_and_unsupported_count_as_agreeing_on_whether_to_flag()
    {
        var s = AgreementStats.Compute([P, U, S], [U, P, S]);

        Assert.Equal(1.0 / 3, s.Exact, 6);
        Assert.Equal(1.0, s.FlagAgreement, 6);
    }

    [Fact]
    public void Confusion_is_indexed_by_first_judge_then_second()
    {
        var s = AgreementStats.Compute([S], [P]);

        Assert.Equal(1, s.Confusion[(int)S, (int)P]);
        Assert.Equal(0, s.Confusion[(int)P, (int)S]);
    }

    [Fact]
    public void Rejects_lists_of_different_length()
    {
        Assert.Throws<ArgumentException>(() => AgreementStats.Compute([S], [S, S]));
    }
}

public class ChunkCoverageTests
{
    [Fact]
    public void Counts_the_share_of_in_scope_chunks_that_some_claim_cites()
    {
        Claim[] claims = [new("a", ["C1"]), new("b", ["C1", "C2"]), new("c", ["FUERA"])];

        // 2 de 4 fragmentos de alcance citados; "FUERA" no cuenta porque no está en el alcance.
        Assert.Equal(0.5, ChunkCoverage.Compute(claims, ["C1", "C2", "C3", "C4"]), 6);
    }

    [Fact]
    public void An_empty_scope_is_not_a_number()
    {
        Assert.True(double.IsNaN(ChunkCoverage.Compute([new Claim("a", ["C1"])], [])));
    }
}

public class CoverageReferenceTests
{
    private static string Path_()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "evaluaciones", "cobertura-orden-hac-1028-2026.json")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "evaluaciones", "cobertura-orden-hac-1028-2026.json");
    }

    [Fact]
    public void The_shipped_reference_is_well_formed_and_calibration_has_both_outcomes()
    {
        var reference = CoverageReference.Load(File.ReadAllText(Path_()));

        Assert.Equal("BOE-A-2026-20587", reference.DocumentId);
        Assert.Equal(35, reference.Facts.Count(f => f.Group == "base"));
        Assert.Equal(16, reference.Facts.Count(f => f.Group == "ampliacion"));
        Assert.Equal(reference.Facts.Count, reference.Facts.Select(f => f.Id).Distinct().Count());
        Assert.All(reference.Facts, f => Assert.StartsWith("BOE-A-2026-20587_", f.ChunkId));

        // Si la calibración solo tuviera un resultado esperado, un medidor que dijera siempre lo mismo la pasaría.
        Assert.Contains(reference.Calibration, c => c.Expected);
        Assert.Contains(reference.Calibration, c => !c.Expected);
    }

    [Fact]
    public void Calibration_using_an_unknown_fact_fails_loudly()
    {
        const string json = """
            {"documento":"D","hechos":[{"id":"f1","fragmento":"D_1","texto":"t"}],
             "calibracion":[{"id":"c1","hecho":"f9","esperado":true,"afirmaciones":["a"]}]}
            """;

        Assert.Throws<InvalidOperationException>(() => CoverageReference.Load(json));
    }
}

public class CoverageExperimentTests
{
    private const string TwoClaims = """
        {"resumen":"R","afirmaciones":[
          {"texto":"Afirmación uno","citas":["DOC_1"]},
          {"texto":"Afirmación dos","citas":["DOC_1"]}]}
        """;

    [Fact]
    public async Task Both_judges_evaluate_exactly_the_same_claims()
    {
        var verifierA = new RecordingVerifier(Support.Supported);
        var verifierB = new RecordingVerifier(Support.Partial);

        var result = await Run(verifierA, verifierB, repetitions: 1);

        // La propiedad que justifica el experimento: mismas afirmaciones, distinto juez.
        // Se compara como conjunto: los verificadores corren en paralelo y el orden de llegada no importa.
        Assert.Equal(["Afirmación dos", "Afirmación uno"], verifierA.Seen.Order(StringComparer.Ordinal));
        Assert.Equal(verifierA.Seen.Order(StringComparer.Ordinal), verifierB.Seen.Order(StringComparer.Ordinal));

        var run = Assert.Single(result.Runs);
        Assert.Equal([Support.Supported, Support.Supported], run.ByJudge["A"].Verdicts);
        Assert.Equal([Support.Partial, Support.Partial], run.ByJudge["B"].Verdicts);
    }

    [Fact]
    public async Task Generates_once_per_repetition_not_once_per_judge()
    {
        var model = new CountingModel(TwoClaims);

        await Run(new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 3, model);

        Assert.Equal(3, model.Generations);
    }

    [Fact]
    public async Task Each_judge_matcher_is_calibrated_and_failures_are_reported()
    {
        var result = await Run(new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 1);

        var a = result.Calibration.Single(c => c.Judge == "A");
        var b = result.Calibration.Single(c => c.Judge == "B");

        // El medidor de A acierta todo; el de B dice siempre "cubierto" y falla el caso esperado "no".
        Assert.Equal(a.Total, a.Correct);
        Assert.Contains("c2", b.Failures);
    }

    [Fact]
    public async Task Fact_coverage_is_recorded_per_judge()
    {
        var result = await Run(new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 1);

        var run = result.Runs[0];
        Assert.Equal(["f1"], run.ByJudge["A"].CoveredFacts);          // A cubre solo f1
        Assert.Equal(["f1", "f2"], run.ByJudge["B"].CoveredFacts);    // B lo da todo por cubierto
        Assert.Equal(1.0, run.ChunkCoverage, 6);                      // DOC_1 está citado
    }

    // ───── montaje ─────

    [Fact]
    public async Task Resuming_keeps_the_saved_runs_and_only_generates_the_missing_ones()
    {
        var first = await Run(new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 2);
        var model = new CountingModel(TwoClaims);

        var resumed = await Run(
            new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 3, model, resume: first);

        Assert.Equal(1, model.Generations);                       // solo la ejecución que faltaba
        Assert.Equal([1, 2, 3], resumed.Runs.Select(r => r.Index)); // numeración continua
        Assert.Equal(first.Calibration, resumed.Calibration);      // la calibración no se repite
    }

    [Fact]
    public async Task Refuses_to_resume_with_different_judges_because_the_runs_would_not_be_comparable()
    {
        var first = await Run(new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 1);
        var other = first with { Judges = ["A", "OTRO"] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(
            new RecordingVerifier(Support.Supported), new RecordingVerifier(Support.Supported), 2, resume: other));
    }

    [Fact]
    public async Task A_saved_result_survives_a_json_round_trip_with_the_cli_options()
    {
        var result = await Run(new RecordingVerifier(Support.Partial), new RecordingVerifier(Support.Supported), 1);
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(result, options);
        var back = System.Text.Json.JsonSerializer.Deserialize<ExperimentResult>(json, options)!;

        Assert.Equal(result.Judges, back.Judges);
        Assert.Equal(result.Runs[0].Claims.Select(c => c.Text), back.Runs[0].Claims.Select(c => c.Text));
        Assert.Equal([Support.Partial, Support.Partial], back.Runs[0].ByJudge["A"].Verdicts);
        Assert.Equal(result.Runs[0].ByJudge["B"].CoveredFacts, back.Runs[0].ByJudge["B"].CoveredFacts);
    }

    private static async Task<ExperimentResult> Run(
        ICitationVerifier a, ICitationVerifier b, int repetitions, ILanguageModel? model = null,
        ExperimentResult? resume = null)
    {
        var analyst = new RegulatoryAnalystAgent(
            model ?? new CountingModel(TwoClaims), new FakeEmbeddings(), new EmptyIndex(),
            new RecordingVerifier(Support.Supported), Options.Create(new ModelOptions()),
            Options.Create(new AnalysisOptions { Mode = AnalysisMode.Single }));

        var experiment = new CoverageExperiment(analyst, new FakeMatcher());

        var reference = new CoverageReference(
            "DOC",
            [new RefFact("f1", "DOC_1", "hecho uno"), new RefFact("f2", "DOC_1", "hecho dos")],
            [new CalibrationItem("c1", "hecho uno", ["lo dice"], true),
             new CalibrationItem("c2", "hecho dos", ["otra cosa"], false)]);

        var change = new RegulatoryChange(
            "DOC", "BOE", "T", new Uri("https://www.boe.es/"), new DateOnly(2026, 10, 5), "texto",
            [new TextChunk("DOC_1", "DOC", "T", "Art. 1", "texto", new DateOnly(2026, 10, 5), new Uri("https://www.boe.es/"))]);

        return await experiment.RunAsync(
            change, reference,
            [new JudgeSpec("A", "juez-a", a), new JudgeSpec("B", "juez-b", b)],
            repetitions, progress: null, default, checkpoint: null, resumeFrom: resume);
    }

    private sealed class RecordingVerifier(Support verdict) : ICitationVerifier
    {
        private readonly List<string> _seen = [];
        public IReadOnlyList<string> Seen { get { lock (_seen) return _seen.ToList(); } }

        public Task<ClaimVerdict> VerifyAsync(Claim claim, IReadOnlyList<TextChunk> cited, CancellationToken ct)
        {
            lock (_seen) _seen.Add(claim.Text);
            return Task.FromResult(new ClaimVerdict(claim, verdict, "r"));
        }
    }

    private sealed class CountingModel(string answer) : ILanguageModel
    {
        public int Generations { get; private set; }

        public Task<string> CompleteAsync(string d, string i, string input, CancellationToken ct)
        {
            Generations++;
            return Task.FromResult(answer);
        }
    }

    // A responde con criterio (solo "hecho uno" está cubierto); B dice siempre que está cubierto.
    private sealed class FakeMatcher : ICoverageMatcher
    {
        public Task<MatchResult> CoversAsync(string deployment, string fact, IReadOnlyList<string> claims, CancellationToken ct)
        {
            var covered = deployment == "juez-b" || fact == "hecho uno";
            return Task.FromResult(covered
                ? new MatchResult(true, [1], "r")
                : new MatchResult(false, [], "r"));
        }
    }

    private sealed class FakeEmbeddings : IEmbeddingModel
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f }).ToList());
    }

    private sealed class EmptyIndex : IChunkIndex
    {
        public Task EnsureCreatedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<IndexedChunk> c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<NormHit>> SearchAsync(string t, float[] v, int top, string? ex, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NormHit>>([]);
    }
}
