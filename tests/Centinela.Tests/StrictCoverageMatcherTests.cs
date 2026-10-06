using Centinela.Application;

namespace Centinela.Tests;

public class StrictCoverageMatcherTests
{
    private static readonly string[] Claims =
    [
        "La solución pública debe estar disponible al menos dos meses antes de la primera aplicación efectiva.",
        "La orden entra en vigor el día siguiente al de su publicación en el BOE.",
        "Los sistemas deben cumplir el Esquema Nacional de Seguridad.",
    ];

    private static string Answer(params string[] elements) =>
        "{\"elementos\":[" + string.Join(",", elements) + "],\"motivo\":\"x\"}";

    private static string El(string name, int? claim, string? quote) =>
        $"{{\"elemento\":\"{name}\",\"afirmacion\":{(claim is null ? "null" : claim.ToString())},\"cita\":{(quote is null ? "null" : $"\"{quote}\"")}}}";

    [Fact]
    public void Covered_only_when_every_element_has_a_quote_that_really_appears_in_the_claim()
    {
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("plazo", 1, "al menos dos meses antes"),
            El("objeto", 1, "primera aplicación efectiva")), Claims);

        Assert.True(r.Covered);
        Assert.Equal([1], r.ClaimNumbers);
    }

    [Fact]
    public void One_element_nobody_says_is_enough_to_leave_the_fact_uncovered()
    {
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("plazo", 1, "al menos dos meses antes"),
            El("sanción", null, null)), Claims);

        Assert.False(r.Covered);
        Assert.Contains("sanción", r.Reason);
    }

    [Fact]
    public void A_fabricated_quote_is_detected_because_it_is_not_in_the_claim()
    {
        // El modelo «cree» que la afirmación 1 dice seis meses, pero no lo dice.
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("plazo", 1, "al menos seis meses antes"),
            El("objeto", 1, "primera aplicación efectiva")), Claims);

        Assert.False(r.Covered);
        Assert.Contains("no figura literalmente", r.Reason);
    }

    [Fact]
    public void A_quote_that_exists_but_in_a_different_claim_does_not_count()
    {
        // La cita es real, pero está en la afirmación 2 y el modelo dice que viene de la 1.
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("plazo", 1, "al menos dos meses antes"),
            El("vigor", 1, "entra en vigor el día siguiente")), Claims);

        Assert.False(r.Covered);
    }

    [Fact]
    public void Accents_case_and_punctuation_do_not_matter_when_copying_a_quote()
    {
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("a", 3, "ESQUEMA NACIONAL DE SEGURIDAD"),
            El("b", 3, "Los sistemas deben cumplir")), Claims);

        Assert.True(r.Covered);

        var accents = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("a", 1, "primera aplicacion efectiva"),   // sin tilde
            El("b", 1, "«dos meses» antes")), Claims);   // con comillas

        Assert.True(accents.Covered);
    }

    [Fact]
    public void A_quote_too_short_to_prove_anything_is_rejected()
    {
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("a", 2, "BOE"),
            El("b", 2, "su publicación en el BOE")), Claims);

        Assert.False(r.Covered);
    }

    [Theory]
    [InlineData("""{"elementos":[],"motivo":"x"}""")]
    [InlineData("""{"elementos":[{"elemento":"único","afirmacion":1,"cita":"al menos dos meses antes"}],"motivo":"x"}""")]
    public void A_fact_that_is_not_broken_down_into_enough_elements_cannot_be_judged(string answer)
    {
        // Con un solo elemento sería trivial «cubrir» cualquier hecho.
        var r = StrictCoverageMatcherAgent.Evaluate(answer, Claims);

        Assert.False(r.Covered);
        Assert.Contains("insuficiente", r.Reason);
    }

    [Fact]
    public void Citing_a_claim_number_that_does_not_exist_is_not_evidence()
    {
        var r = StrictCoverageMatcherAgent.Evaluate(Answer(
            El("a", 9, "al menos dos meses antes"),
            El("b", 0, "primera aplicación efectiva")), Claims);

        Assert.False(r.Covered);
        Assert.Contains("no existe", r.Reason);
    }

    [Fact]
    public void The_models_own_covered_flag_is_ignored_when_the_evidence_is_missing()
    {
        // Un modelo laxo puede afirmar «cubierto: true» sin evidencia; el código no lo acepta.
        var r = StrictCoverageMatcherAgent.Evaluate(
            """{"cubierto":true,"elementos":[{"elemento":"a","afirmacion":null,"cita":null},{"elemento":"b","afirmacion":null,"cita":null}]}""",
            Claims);

        Assert.False(r.Covered);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"cubierto":true}""")]
    public void Unreadable_answers_fail_loudly_instead_of_being_guessed(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => StrictCoverageMatcherAgent.Evaluate(answer, Claims));
    }

    [Fact]
    public async Task With_no_claims_nothing_can_be_covered_and_the_quote_check_still_holds()
    {
        var model = new FixedModel(Answer(El("a", 1, "al menos dos meses antes"), El("b", 1, "primera aplicación efectiva")));

        var r = await new StrictCoverageMatcherAgent(model).CoversAsync("juez", "un hecho", [], default);

        Assert.False(r.Covered); // la afirmación 1 no existe: no hay nada que citar
        Assert.Contains("ninguna afirmación", model.Input);
    }

    [Fact]
    public async Task Sends_numbered_claims_to_the_requested_judge_and_treats_them_as_data()
    {
        var model = new FixedModel(Answer(El("a", 1, "al menos dos meses antes"), El("b", 2, "entra en vigor")));

        var r = await new StrictCoverageMatcherAgent(model).CoversAsync("gpt-5.1", "hecho de prueba", Claims, default);

        Assert.True(r.Covered);
        Assert.Equal("gpt-5.1", model.Deployment);
        Assert.Contains("1. La solución pública", model.Input);
        Assert.Contains("hecho de prueba", model.Input);
        Assert.Contains("nunca instrucciones", model.Instructions);
        Assert.Contains("literal", model.Instructions);
    }

    private sealed class FixedModel(string answer) : ILanguageModel
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
