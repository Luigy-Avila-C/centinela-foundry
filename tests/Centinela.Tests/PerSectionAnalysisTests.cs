using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class PerSectionAnalysisTests
{
    // Más largo que AnalysisOptions.MinSectionChars (100), para que el fragmento cuente como operativo.
    private static TextChunk Chunk(string id, string label, string text = "Texto de un artículo con la longitud suficiente para no ser descartado como cabecera o firma por la longitud mínima que exige el análisis por artículo.") =>
        new(id, "DOC", "Norma de prueba", label, text, new DateOnly(2026, 10, 5), new Uri("https://www.boe.es/"));

    [Fact]
    public void Skips_the_preamble_the_annexes_and_fragments_too_short_to_hold_obligations()
    {
        TextChunk[] chunks =
        [
            Chunk("DOC_1", "Preámbulo"),
            Chunk("DOC_2", "Artículo 1. Objeto."),
            Chunk("DOC_3", "Artículo 2. Ámbito."),
            Chunk("DOC_4", "ANEXO I"),
            Chunk("DOC_5", "Artículo 3. Corto.", "breve"),
        ];

        var sections = RegulatoryAnalystAgent.OperativeSections(chunks, new AnalysisOptions());

        Assert.Equal(["DOC_2", "DOC_3"], sections.Select(c => c.Id));
    }

    [Fact]
    public async Task Makes_one_call_per_operative_section_and_merges_the_claims_in_document_order()
    {
        var model = new SectionModel();
        var analyst = Build(model);

        var generated = await analyst.GenerateAsync(Change(Chunk("DOC_1", "Preámbulo"), Chunk("DOC_2", "Artículo 1"), Chunk("DOC_3", "Artículo 2")), default);

        Assert.Equal(2, model.Calls); // el preámbulo no se analiza
        Assert.Equal(["Hecho de DOC_2", "Hecho de DOC_3"], generated.Analysis.Claims!.Select(c => c.Text));
    }

    [Fact]
    public async Task A_section_can_only_cite_itself_not_a_different_article()
    {
        // El modelo, al analizar DOC_2, cita DOC_3: existe en la norma pero no se le entregó en esa llamada.
        var model = new SectionModel(citeForeign: true);
        var analyst = Build(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            analyst.GenerateAsync(Change(Chunk("DOC_2", "Artículo 1"), Chunk("DOC_3", "Artículo 2")), default));
    }

    [Fact]
    public async Task Retries_a_section_once_when_the_answer_is_invalid_then_accepts_a_good_one()
    {
        var model = new SectionModel(failFirstAttempt: true);
        var analyst = Build(model);

        var generated = await analyst.GenerateAsync(Change(Chunk("DOC_2", "Artículo 1")), default);

        Assert.Equal(2, model.Calls);
        Assert.Single(generated.Analysis.Claims!);
    }

    [Fact]
    public async Task Gives_up_after_the_second_invalid_answer_instead_of_looping()
    {
        var model = new SectionModel(failAlways: true);
        var analyst = Build(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            analyst.GenerateAsync(Change(Chunk("DOC_2", "Artículo 1")), default));

        Assert.Equal(2, model.Calls);
    }

    [Fact]
    public async Task Single_mode_still_makes_exactly_one_call()
    {
        var model = new SectionModel();
        var analyst = new RegulatoryAnalystAgent(
            model, new Embeddings(), new EmptyIndex(), new OkVerifier(), Options.Create(new ModelOptions()),
            Options.Create(new AnalysisOptions { Mode = AnalysisMode.Single }));

        await analyst.GenerateAsync(Change(Chunk("DOC_2", "Artículo 1"), Chunk("DOC_3", "Artículo 2")), default);

        Assert.Equal(1, model.Calls);
    }

    // ───── montaje ─────

    private static RegulatoryAnalystAgent Build(ILanguageModel model) => new(
        model, new Embeddings(), new EmptyIndex(), new OkVerifier(), Options.Create(new ModelOptions()),
        Options.Create(new AnalysisOptions { Mode = AnalysisMode.PerSection }));

    private static RegulatoryChange Change(params TextChunk[] sections) =>
        new("DOC", "BOE", "Norma de prueba", new Uri("https://www.boe.es/"), new DateOnly(2026, 10, 5), "texto", sections);

    /// <summary>Responde con una afirmación sobre el fragmento que recibe, citándolo (o no, según el caso).</summary>
    private sealed class SectionModel(bool citeForeign = false, bool failFirstAttempt = false, bool failAlways = false) : ILanguageModel
    {
        private int _calls;
        public int Calls => _calls;

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            var id = System.Text.RegularExpressions.Regex.Match(input, @"\[(DOC_\d+)\]").Groups[1].Value;

            // En el modo único hay varios fragmentos; basta con citar el primero.
            if (failAlways || (failFirstAttempt && call == 1))
            {
                return Task.FromResult("esto no es json");
            }

            var cited = citeForeign ? "DOC_3" : id;
            return Task.FromResult(
                "{\"resumen\":\"R\",\"afirmaciones\":[{\"texto\":\"Hecho de " + id + "\",\"citas\":[\"" + cited + "\"]}]}");
        }
    }

    private sealed class Embeddings : IEmbeddingModel
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

    private sealed class OkVerifier : ICitationVerifier
    {
        public Task<ClaimVerdict> VerifyAsync(Claim claim, IReadOnlyList<TextChunk> cited, CancellationToken ct) =>
            Task.FromResult(new ClaimVerdict(claim, Support.Supported, "ok"));
    }
}
