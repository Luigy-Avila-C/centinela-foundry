using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Tests;

public class ProblemPersistsTests
{
    // Artefactos reales de una ejecución del flujo (evaluaciones/resultados/caso-aurora.md).
    private const string RealProblemQuote =
        "administración corrige el PDF, conserva el mismo número y se lo reenvía al cliente por correo electrónico. No se emite una nueva factura.";

    private const string RealDraftThatKeptThePracticeAndAddedTheOpposite =
        "3. Errores de datos detectados por el cliente\n" +
        "Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), administración corrige el PDF, conserva el mismo número y se lo reenvía al cliente por correo electrónico. No se emite una nueva factura.\n" +
        "Si el rechazo de la copia fiel se debe a errores en la factura electrónica original, administración emitirá una factura electrónica original correcta y la remitirá al cliente por correo electrónico.";

    private const string EmissionQuote =
        "No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo.";

    private const string RealDraftThatModifiedTheSentence =
        "Simultáneamente a su emisión, se genera una copia electrónica fiel de cada factura en la sintaxis UBL y se remite a la solución pública de facturación electrónica. " +
        "No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo, salvo la copia electrónica fiel en sintaxis UBL remitida a la solución pública de facturación electrónica.";

    [Fact]
    public void A_draft_that_keeps_the_violating_sentence_and_adds_the_opposite_underneath_is_flagged()
    {
        Assert.True(DraftChecks.SentenceSurvives(RealProblemQuote, RealDraftThatKeptThePracticeAndAddedTheOpposite));
    }

    [Fact]
    public void A_sentence_that_was_modified_with_an_exception_is_not_the_same_sentence()
    {
        // Añadir «, salvo …» a la frase es una corrección legítima; marcarla sería una falsa alarma.
        Assert.False(DraftChecks.SentenceSurvives(EmissionQuote, RealDraftThatModifiedTheSentence));
    }

    [Theory]
    [InlineData("Esto se queda tal cual. Lo demás cambia.", "Esto se queda tal cual", true)]    // acaba en punto
    [InlineData("Esto se queda tal cual\nLo demás cambia.", "Esto se queda tal cual", true)]    // acaba en salto de línea
    [InlineData("Esto se queda tal cual", "Esto se queda tal cual", true)]                       // acaba el texto
    [InlineData("Esto se queda tal cual, aunque con matices.", "Esto se queda tal cual", false)] // coma: continúa
    [InlineData("Esto se queda tal cual y además se amplía.", "Esto se queda tal cual", false)]  // minúscula: continúa
    [InlineData("Otra cosa distinta.", "Esto se queda tal cual", false)]
    [InlineData("ESTO SE QUEDA TAL CUAL. Fin.", "esto se queda tal cual.", true)]               // sin distinguir mayúsculas ni punto final
    [InlineData("Muy poco.", "Muy poco", false)]                                                  // cita demasiado corta: no concluye nada
    public void Sentence_survival_depends_on_how_the_sentence_continues(string proposed, string quote, bool expected)
    {
        Assert.Equal(expected, DraftChecks.SentenceSurvives(quote, proposed));
    }

    private static CorrectiveAction Action(string text) => new(
        "DOC", text, "j", null, "P", "Sec", "original", ["N"],
        [new ObligationCoverage("Debe emitir una factura correcta.", "c", "emite una factura correcta")], []);

    [Theory]
    [InlineData("incumple", true)]
    [InlineData("falta_requisito", false)] // la frase puede seguir: se le añade lo que falta
    [InlineData("desfasado", false)]
    [InlineData(null, false)]
    public void Only_a_violation_requires_the_sentence_to_disappear(string? effect, bool flagged)
    {
        var issues = DraftChecks.Run(
            Action("Esto se queda tal cual. Además administración emite una factura correcta."),
            ["Debe emitir una factura correcta."], ["N"], "",
            problemQuote: "Esto se queda tal cual.", problemEffect: effect);

        Assert.Equal(flagged, issues.Any(i => i.Type == "problema_persiste"));
    }

    [Fact]
    public void A_draft_that_replaces_the_violating_sentence_is_not_flagged()
    {
        var issues = DraftChecks.Run(
            Action("Ahora administración emite una factura correcta."),
            ["Debe emitir una factura correcta."], ["N"], "",
            problemQuote: "Esto se queda tal cual.", problemEffect: "incumple");

        Assert.DoesNotContain(issues, i => i.Type == "problema_persiste");
    }

    [Fact]
    public void The_issue_is_blocking_and_quotes_the_offending_sentence()
    {
        var issue = DraftChecks.Run(
                Action("Esto se queda tal cual. Además administración emite una factura correcta."),
                ["Debe emitir una factura correcta."], ["N"], "",
                problemQuote: "Esto se queda tal cual.", problemEffect: "incumple")
            .Single(i => i.Type == "problema_persiste");

        Assert.True(issue.Blocking);
        Assert.Equal("Esto se queda tal cual.", issue.Quote);
    }
}

public class ImpactScopeTests
{
    private static readonly TextChunk Passage = new(
        "P_1", "DOC", "Documento", "2. Pagos", "Administración no comunica la fecha de pago. Y rechaza por teléfono.",
        new DateOnly(2026, 1, 1), new Uri("file:///x"));

    private static readonly Claim[] Shown =
    [
        new("El destinatario debe informar del rechazo y de la fecha de pago.", ["N_1"]),
        new("Otra obligación distinta sobre facturas.", ["N_2"]),
    ];

    private static string Hallazgo(int n, string requirement, string effect, string quote) =>
        "{\"obligacion\":" + n + ",\"exigencia\":\"" + requirement + "\",\"sujeto\":\"empresa\",\"tipo\":\"deber\",\"base\":\"afirma\"," +
        "\"efecto\":\"" + effect + "\",\"gravedad\":\"alta\",\"cita\":\"" + quote + "\",\"motivo\":\"m\",\"accion\":\"a\"}";

    [Fact]
    public void The_finding_keeps_the_effect_of_the_most_severe_hallazgo_and_the_scope_per_obligation()
    {
        var answer = "{\"hallazgos\":[" +
            Hallazgo(1, "informar del rechazo", "falta_requisito", "rechaza por teléfono") + "," +
            Hallazgo(2, "obligación distinta", "incumple", "no comunica la fecha de pago") + "]}";

        var f = ImpactAgent.ParseOutcome(answer, Passage, Shown).Finding!;

        Assert.Equal(["N_1", "N_2"], f.NormCitations!.Order());
        Assert.Equal(Shown.Select(s => s.Text), f.Obligations);
        // Cada obligación lleva el fragmento que de verdad se exige a este pasaje, en la misma posición.
        Assert.Equal(["informar del rechazo", "obligación distinta"], f.Requirements);
        Assert.NotNull(f.PassageEffect);
    }

    [Fact]
    public void An_obligation_appearing_twice_is_kept_once_with_its_first_scope()
    {
        var answer = "{\"hallazgos\":[" +
            Hallazgo(1, "informar del rechazo", "incumple", "rechaza por teléfono") + "," +
            Hallazgo(1, "de la fecha de pago", "incumple", "no comunica la fecha de pago") + "]}";

        var f = ImpactAgent.ParseOutcome(answer, Passage, Shown).Finding!;

        Assert.Single(f.Obligations!);
        Assert.Equal(["informar del rechazo"], f.Requirements);
        Assert.Equal(f.Obligations!.Count, f.Requirements!.Count); // siempre alineadas
    }
}

public class ShippedScopeDatasetTests
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
    public void Every_problem_quote_is_a_literal_part_of_its_original_passage()
    {
        var passages = Passages();

        foreach (var f in Cases().Select(c => c.Finding).DistinctBy(f => f.Id))
        {
            Assert.NotNull(f.ProblemQuote);
            Assert.NotNull(f.ProblemEffect);
            Assert.True(
                QuoteMatch.Appears(f.ProblemQuote!, passages[(f.Document, f.Section)].Text),
                $"{f.Id}: la frase problemática no está en el pasaje");
        }
    }

    [Fact]
    public void Every_scope_fragment_is_a_literal_part_of_its_obligation()
    {
        foreach (var f in Cases().Select(c => c.Finding).DistinctBy(f => f.Id).Where(f => f.Scope is not null))
        {
            Assert.Equal(f.Obligations.Count, f.Scope!.Count);
            for (var i = 0; i < f.Obligations.Count; i++)
            {
                Assert.True(QuoteMatch.Appears(f.Scope[i], f.Obligations[i]), $"{f.Id}: alcance {i + 1}");
            }
        }
    }

    [Fact]
    public void No_good_draft_keeps_the_violating_sentence()
    {
        // Si un borrador bueno la conservara, la nueva comprobación daría una falsa alarma sobre el conjunto medido.
        foreach (var c in Cases().Where(c => c.ShouldApprove))
        {
            Assert.False(
                c.Finding.ProblemEffect == "incumple" && DraftChecks.SentenceSurvives(c.Finding.ProblemQuote!, c.Text),
                $"{c.Id} conserva la frase problemática");
        }
    }

    [Fact]
    public void The_new_check_does_not_change_which_flawed_drafts_the_code_alone_catches()
    {
        var passages = Passages();
        var byCode = new List<string>();

        foreach (var c in Cases().Where(c => !c.ShouldApprove))
        {
            var original = passages[(c.Finding.Document, c.Finding.Section)].Text;
            var blocking = DraftChecks.Run(
                    AuditorEvaluation.ToAction(c, "P", original), c.Finding.Obligations, c.Finding.NormIds, "",
                    c.Finding.ProblemQuote, c.Finding.ProblemEffect)
                .Any(i => i.Blocking);

            if (blocking) byCode.Add(c.Id);
        }

        // Ningún borrador defectuoso del conjunto conserva la frase completa, así que las cifras medidas del auditor
        // no cambian. La comprobación se justifica con el fallo real de la ejecución del flujo, no con este conjunto.
        Assert.Equal(["b03", "b06", "b09", "b12", "b14", "b20", "b21"], byCode.Order());
    }
}
