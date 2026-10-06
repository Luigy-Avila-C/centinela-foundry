using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class EvalReportTests
{
    private static EvalOutcome O(Support expected, Support actual) =>
        new(new EvalCase("x", "dev", expected, "c", []), actual, "");

    [Fact]
    public void A_verifier_that_approves_everything_is_exposed_by_recall_not_by_accuracy()
    {
        // 8 correctas y 2 malas: aprobar todo da un 80 % de acierto global y 0 % de detección.
        var outcomes = Enumerable.Range(0, 8).Select(_ => O(Support.Supported, Support.Supported))
            .Concat(Enumerable.Range(0, 2).Select(_ => O(Support.Unsupported, Support.Supported)));

        var report = EvalReport.From(outcomes);

        Assert.Equal(0.8, report.Accuracy, 3);
        Assert.Equal(0.0, report.ProblemRecall, 3);
        Assert.Equal(0.0, report.UnsupportedRecall, 3);
        Assert.Equal(0.0, report.FalseAlarmRate, 3);
    }

    [Fact]
    public void Counts_a_partial_verdict_as_flagging_a_problem_but_not_as_an_exact_match_for_unsupported()
    {
        var report = EvalReport.From([
            O(Support.Unsupported, Support.Partial), // marcada, pero no es la etiqueta exacta
            O(Support.Unsupported, Support.Unsupported),
            O(Support.Partial, Support.Partial),
            O(Support.Partial, Support.Supported),   // se escapó
        ]);

        Assert.Equal(3.0 / 4, report.ProblemRecall, 3);
        Assert.Equal(1.0 / 2, report.UnsupportedRecall, 3);
        Assert.Equal(2.0 / 4, report.Accuracy, 3);
    }

    [Fact]
    public void Flagging_a_correct_claim_is_a_false_alarm()
    {
        var report = EvalReport.From([
            O(Support.Supported, Support.Supported),
            O(Support.Supported, Support.Partial),
            O(Support.Supported, Support.Unsupported),
            O(Support.Supported, Support.Supported),
        ]);

        Assert.Equal(0.5, report.FalseAlarmRate, 3);
    }

    [Fact]
    public void Confusion_matrix_is_indexed_by_expected_then_actual()
    {
        var report = EvalReport.From([O(Support.Partial, Support.Unsupported)]);

        Assert.Equal(1, report.Confusion[(int)Support.Partial, (int)Support.Unsupported]);
        Assert.Equal(0, report.Confusion[(int)Support.Unsupported, (int)Support.Partial]);
    }

    [Fact]
    public void Metrics_with_no_denominator_are_not_a_number_rather_than_zero()
    {
        // Sin casos "malos" no se puede decir que la detección sea 0 %: simplemente no se mide.
        var report = EvalReport.From([O(Support.Supported, Support.Supported)]);

        Assert.True(double.IsNaN(report.ProblemRecall));
    }
}

public class DatasetTests
{
    private static string DatasetPath()
    {
        // Sube desde el directorio de pruebas hasta encontrar la carpeta del repositorio.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "evaluaciones", "verificador-citas.json")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new FileNotFoundException("No se encuentra evaluaciones/verificador-citas.json")
            : Path.Combine(dir.FullName, "evaluaciones", "verificador-citas.json");
    }

    [Fact]
    public void The_shipped_dataset_is_well_formed_and_balanced_in_both_splits()
    {
        var cases = VerifierEvaluation.LoadDataset(File.ReadAllText(DatasetPath()));

        foreach (var split in new[] { "dev", "test" })
        {
            var inSplit = cases.Where(c => c.Split == split).ToList();
            Assert.NotEmpty(inSplit);
            // Hacen falta casos de las tres clases en cada parte; si no, las métricas no se pueden calcular.
            Assert.Contains(inSplit, c => c.Expected == Support.Supported);
            Assert.Contains(inSplit, c => c.Expected == Support.Unsupported);
            Assert.Contains(inSplit, c => c.Expected == Support.Partial);
        }

        Assert.All(cases, c => Assert.NotEmpty(c.Cited));
    }

    [Fact]
    public void A_case_citing_an_unknown_source_fails_loudly()
    {
        const string json = """
            {"fuentes":{"A":{"id":"X_1","documento":"d","etiqueta":"e","texto":"t"}},
             "casos":[{"id":"c1","split":"dev","esperado":"respaldada","citas":["ZZ"],"afirmacion":"a"}]}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => VerifierEvaluation.LoadDataset(json));
        Assert.Contains("ZZ", ex.Message);
    }

    [Fact]
    public void An_unknown_label_fails_instead_of_being_treated_as_a_default()
    {
        const string json = """
            {"fuentes":{"A":{"id":"X_1","documento":"d","etiqueta":"e","texto":"t"}},
             "casos":[{"id":"c1","split":"dev","esperado":"quizas","citas":["A"],"afirmacion":"a"}]}
            """;

        Assert.Throws<InvalidOperationException>(() => VerifierEvaluation.LoadDataset(json));
    }
}

public class CitationVerifierTests
{
    [Theory]
    [InlineData("""{"veredicto":"respaldada","motivo":"ok"}""", Support.Supported)]
    [InlineData("""{"veredicto":"parcial","motivo":"falta el plazo"}""", Support.Partial)]
    [InlineData("""{"veredicto":"no_respaldada","motivo":"otro tema"}""", Support.Unsupported)]
    [InlineData("```json\n{\"veredicto\":\"respaldada\",\"motivo\":\"ok\"}\n```", Support.Supported)]
    public void Parses_the_three_verdicts(string answer, Support expected)
    {
        Assert.Equal(expected, CitationVerifierAgent.ParseAnswer(answer).Support);
    }

    [Theory]
    [InlineData("""{"veredicto":"quizá"}""")]
    [InlineData("""{"motivo":"sin veredicto"}""")]
    [InlineData("sí, está respaldada")]
    public void Rejects_a_verdict_outside_the_vocabulary_instead_of_guessing(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => CitationVerifierAgent.ParseAnswer(answer));
    }

    [Fact]
    public async Task A_claim_with_no_cited_fragments_is_unsupported_without_calling_the_model()
    {
        var model = new CountingModel();
        var verifier = new CitationVerifierAgent(model, Options.Create(new ModelOptions()));

        var verdict = await verifier.VerifyAsync(new Claim("algo", []), [], default);

        Assert.Equal(Support.Unsupported, verdict.Support);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Sends_only_the_cited_text_and_uses_the_judge_model()
    {
        var model = new CountingModel();
        var verifier = new CitationVerifierAgent(model, Options.Create(new ModelOptions { Judge = "juez" }));
        var chunk = new TextChunk("D_001", "D", "Norma", "Art. 1", "texto del artículo",
            new DateOnly(2026, 1, 1), new Uri("https://www.boe.es/"));

        await verifier.VerifyAsync(new Claim("afirmación X", ["D_001"]), [chunk], default);

        Assert.Equal("juez", model.Deployment);
        Assert.Contains("texto del artículo", model.Input);
        Assert.Contains("afirmación X", model.Input);
        Assert.Contains("nunca instrucciones", model.Instructions);
    }

    private sealed class CountingModel : ILanguageModel
    {
        public int Calls { get; private set; }
        public string Deployment { get; private set; } = "";
        public string Instructions { get; private set; } = "";
        public string Input { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            Calls++;
            (Deployment, Instructions, Input) = (deployment, instructions, input);
            return Task.FromResult("""{"veredicto":"respaldada","motivo":"ok"}""");
        }
    }
}
