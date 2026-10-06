using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class AuditorVotingTests
{
    private const string Original = "Pagamos a 60 días. No comunicamos el rechazo de las facturas a nadie.";
    private const string Draft = "Pagamos a 60 días. Administración informará del rechazo de las facturas a la solución pública.";

    private static AuditInput Input() => new(
        new CorrectiveAction("DOC", Draft, "j", null, "P_1", "Pagos", Original, ["N_1"],
            [new ObligationCoverage("Debe informar del rechazo.", "c", "informará del rechazo de las facturas")], []),
        ["Debe informar del rechazo."], ["N_1"],
        [new TextChunk("N_1", "N", "Norma", "Art. 1", "Texto", new DateOnly(2026, 1, 1), new Uri("https://www.boe.es/"))],
        "Norma de prueba");

    private const string NoIssues = """{"incidencias":[]}""";
    private const string NotCovered = """{"incidencias":[{"tipo":"no_cubre","obligacion":1,"cita":null,"descripcion":"No cubre."}]}""";
    private const string Invented = """{"incidencias":[{"tipo":"dato_inventado","cita":"informará del rechazo","descripcion":"Inventa."}]}""";

    private static AuditorAgent Auditor(int votes, params string[] answers) => new(
        new ScriptedModel(answers), Options.Create(new ModelOptions()), Options.Create(new AuditorOptions { Votes = votes }));

    [Fact]
    public async Task One_vote_behaves_as_before_and_makes_a_single_call()
    {
        var model = new ScriptedModel([NotCovered]);

        var r = await new AuditorAgent(model, Options.Create(new ModelOptions())).AuditOneAsync(Input(), default);

        Assert.False(r.Passed);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task An_issue_raised_by_a_single_vote_out_of_three_is_dropped_as_noise()
    {
        var r = await Auditor(3, NotCovered, NoIssues, NoIssues).AuditOneAsync(Input(), default);

        Assert.True(r.Passed);
    }

    [Fact]
    public async Task An_issue_raised_by_two_votes_out_of_three_is_kept()
    {
        var r = await Auditor(3, NotCovered, NoIssues, NotCovered).AuditOneAsync(Input(), default);

        Assert.False(r.Passed);
        Assert.Contains(r.Issues, i => i.Type == "no_cubre" && i.Origin == "modelo");
    }

    [Fact]
    public async Task Different_issue_types_are_voted_separately()
    {
        // Cada tipo aparece en un solo voto: ninguno alcanza la mayoría.
        var r = await Auditor(3, NotCovered, Invented, NoIssues).AuditOneAsync(Input(), default);

        Assert.True(r.Passed);
    }

    [Fact]
    public async Task Deterministic_code_checks_are_never_voted_away()
    {
        var withInventedNumber = Input() with
        {
            Action = Input().Action with { ProposedText = Draft + " En 48 horas." },
        };

        // Los tres votos del modelo dicen «sin incidencias»: la cifra inventada la ve el código igualmente.
        var r = await Auditor(3, NoIssues, NoIssues, NoIssues).AuditOneAsync(withInventedNumber, default);

        Assert.False(r.Passed);
        Assert.Contains(r.Issues, i => i.Type == "dato_inventado" && i.Origin == "automatica");
    }

    [Fact]
    public async Task Three_votes_make_three_model_calls_per_audit()
    {
        var model = new ScriptedModel([NoIssues]);

        await new AuditorAgent(model, Options.Create(new ModelOptions()), Options.Create(new AuditorOptions { Votes = 3 }))
            .AuditOneAsync(Input(), default);

        Assert.Equal(3, model.Calls);
    }

    [Fact]
    public void Majority_votes_no_cubre_per_obligation_so_two_different_obligations_do_not_add_up()
    {
        var a = new List<DraftIssue> { new("no_cubre", "x", Obligation: 1, Origin: "modelo") };
        var b = new List<DraftIssue> { new("no_cubre", "y", Obligation: 2, Origin: "modelo") };
        var c = new List<DraftIssue>();

        Assert.Empty(AuditorAgent.Majority([a, b, c]));
        Assert.Single(AuditorAgent.Majority([a, a, c]));
    }

    /// <summary>Devuelve las respuestas en orden; la última se repite si hay más llamadas que respuestas.</summary>
    private sealed class ScriptedModel(string[] answers) : ILanguageModel
    {
        private int _calls;
        public int Calls => _calls;

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            var i = Interlocked.Increment(ref _calls) - 1;
            return Task.FromResult(answers[Math.Min(i, answers.Length - 1)]);
        }
    }
}
