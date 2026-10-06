using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.Options;

namespace Centinela.Tests;

public class NormChunkerTests
{
    private static SourceDocument Doc(params (bool Starts, string Text)[] paragraphs) =>
        new("BOE-A-2026-1", "Orden de prueba", "Orden", "Hacienda", new DateOnly(2026, 10, 5),
            new Uri("https://www.boe.es/x"),
            paragraphs.Select(p => new SourceParagraph(p.Starts, p.Text)).ToList());

    [Fact]
    public void Splits_by_article_and_puts_prior_text_in_a_preamble()
    {
        var chunks = DocumentChunker.Chunk(Doc(
            (false, "Exposición de motivos."),
            (true, "Artículo 1. Objeto."), (false, "Esta orden regula..."),
            (true, "Artículo 2. Ámbito."), (false, "Se aplica a...")));

        Assert.Equal(["Preámbulo", "Artículo 1. Objeto.", "Artículo 2. Ámbito."], chunks.Select(c => c.Label));
        Assert.Contains("Esta orden regula", chunks[1].Text);
    }

    [Fact]
    public void Chunk_ids_are_valid_search_keys_and_unique()
    {
        var chunks = DocumentChunker.Chunk(Doc((true, "Artículo 1."), (true, "Artículo 2."), (true, "Artículo 3.")));

        Assert.Equal(chunks.Count, chunks.Select(c => c.Id).Distinct().Count());
        // Azure AI Search solo admite letras, dígitos, "_", "-" y "=" en las claves.
        Assert.All(chunks, c => Assert.Matches("^[A-Za-z0-9_=-]+$", c.Id));
    }

    [Fact]
    public void Long_articles_are_split_so_no_chunk_exceeds_the_limit()
    {
        var paragraphs = Enumerable.Range(0, 20).Select(i => (false, new string('x', 300))).ToList();
        paragraphs.Insert(0, (true, "Artículo 1. Largo."));

        var chunks = DocumentChunker.Chunk(Doc(paragraphs.ToArray()), maxChars: 1000);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1000));
        Assert.All(chunks, c => Assert.Equal("Artículo 1. Largo.", c.Label));
    }

    [Fact]
    public void Empty_document_yields_no_chunks()
    {
        Assert.Empty(DocumentChunker.Chunk(Doc()));
    }
}

public class BoeDocumentParsingTests
{
    private const string Xml = """
        <documento>
          <metadatos>
            <identificador>BOE-A-2026-20587</identificador>
            <departamento>Ministerio de Hacienda</departamento>
            <rango>Orden</rango>
            <titulo>Orden HAC/1028/2026 de prueba</titulo>
            <fecha_publicacion>20261005</fecha_publicacion>
          </metadatos>
          <texto>
            <p class="parrafo">Preámbulo   con
               saltos.</p>
            <p class="articulo">Artículo 1. Objeto.</p>
            <p class="parrafo">Esta orden regula.</p>
            <table class="tabla"><tr><td>A</td><td>B</td></tr></table>
            <p class="anexo_num">ANEXO I</p>
            <p class="parrafo">  </p>
          </texto>
        </documento>
        """;

    [Fact]
    public void Reads_metadata_and_marks_articles_and_annexes_as_section_starts()
    {
        var doc = BoeClient.ParseDocument(Xml);

        Assert.Equal("BOE-A-2026-20587", doc.Id);
        Assert.Equal(new DateOnly(2026, 10, 5), doc.PublishedOn);
        Assert.Equal("Orden", doc.Rank);
        Assert.Equal(
            ["Preámbulo con saltos.", "Artículo 1. Objeto.", "Esta orden regula.", "AB", "ANEXO I"],
            doc.Paragraphs.Select(p => p.Text));
        Assert.Equal([false, true, false, false, true], doc.Paragraphs.Select(p => p.StartsSection));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("BOE-A-2026-1?x=1")]
    [InlineData("")]
    public async Task Rejects_identifiers_that_are_not_boe_ids(string id)
    {
        var client = new BoeClient(new HttpClient());

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDocumentAsync(id, default));
    }
}

public class RegulatoryAnalystAgentTests
{
    private static readonly IReadOnlySet<string> Citable = new HashSet<string> { "DOC_001", "DOC_002" };

    private static string Answer(string claimsJson) =>
        "{\"resumen\":\"Resumen.\",\"afirmaciones\":" + claimsJson + "}";

    [Fact]
    public void Accepts_an_analysis_where_every_claim_cites_existing_fragments()
    {
        var analysis = RegulatoryAnalystAgent.ParseAnswer(Answer(
            """[{"texto":"A","citas":["DOC_001"]},{"texto":"B","citas":["DOC_001","DOC_002"]}]"""), Citable);

        Assert.Equal(2, analysis.Claims!.Count);
        Assert.Equal(["DOC_001", "DOC_002"], analysis.Citations); // unión sin duplicados
    }

    [Fact]
    public void Rejects_the_whole_analysis_when_one_claim_has_no_citation()
    {
        // Es el hueco que motivó esta comprobación: antes bastaba con citar algo en cualquier sitio.
        var ex = Assert.Throws<InvalidOperationException>(() => RegulatoryAnalystAgent.ParseAnswer(Answer(
            """[{"texto":"Con cita","citas":["DOC_001"]},{"texto":"Sin cita","citas":[]}]"""), Citable));

        Assert.Contains("sin cita", ex.Message);
        Assert.Contains("Sin cita", ex.Message);
    }

    [Fact]
    public void Rejects_a_claim_whose_citations_field_is_missing()
    {
        Assert.Throws<InvalidOperationException>(() => RegulatoryAnalystAgent.ParseAnswer(Answer(
            """[{"texto":"Sin campo citas"}]"""), Citable));
    }

    [Fact]
    public void Rejects_the_whole_analysis_when_a_citation_is_invented()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RegulatoryAnalystAgent.ParseAnswer(Answer(
            """[{"texto":"A","citas":["DOC_001"]},{"texto":"B","citas":["DOC_999"]}]"""), Citable));

        Assert.Contains("DOC_999", ex.Message);
    }

    [Fact]
    public void Rejects_an_analysis_with_no_claims()
    {
        Assert.Throws<InvalidOperationException>(() => RegulatoryAnalystAgent.ParseAnswer(Answer("[]"), Citable));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"resumen":"","afirmaciones":[{"texto":"A","citas":["DOC_001"]}]}""")]
    [InlineData("""{"resumen":"R"}""")]
    public void Rejects_unreadable_or_empty_answers(string answer)
    {
        Assert.Throws<InvalidOperationException>(() => RegulatoryAnalystAgent.ParseAnswer(answer, Citable));
    }

    [Fact]
    public async Task Verifies_each_cited_claim_against_only_the_fragments_it_cites()
    {
        var index = new FakeIndex([
            new NormHit(new TextChunk("PREV_001", "PREV", "Norma previa", "Art. 5", "texto previo",
                new DateOnly(2012, 1, 1), new Uri("https://www.boe.es/p")), 1.0)]);
        var model = new FakeModel(Answer(
            """[{"texto":"Del cambio","citas":["BOE-A-1"]},{"texto":"De la previa","citas":["PREV_001"]}]"""));
        var verifier = new FakeVerifier();
        var agent = new RegulatoryAnalystAgent(
            model, new FakeEmbeddings(), index, verifier, Options.Create(new ModelOptions { Smart = "potente" }),
            Options.Create(new AnalysisOptions { Mode = AnalysisMode.Single }));

        var change = new RegulatoryChange(
            "BOE-A-1", "BOE", "Cambio", new Uri("https://www.boe.es/c"), new DateOnly(2026, 10, 5), "texto");

        var analysis = await agent.AnalyzeAsync(change, default);

        Assert.Equal("BOE-A-1", index.ExcludedDocumentId);
        Assert.Equal("potente", model.Deployment);
        Assert.Contains("[PREV_001]", model.Input);

        // El verificador recibe cada afirmación con SUS fragmentos, no con todo el contexto.
        Assert.Equal(["BOE-A-1"], verifier.Calls["Del cambio"]);
        Assert.Equal(["PREV_001"], verifier.Calls["De la previa"]);
        Assert.Equal(2, analysis.Verdicts!.Count);

        // Los artículos afectados salen de lo citado, no de lo que el modelo diga.
        Assert.Equal(["Texto completo", "PREV · Art. 5"], analysis.AffectedArticles);
    }

    [Fact]
    public async Task Exposes_the_claims_the_verifier_considers_unsupported()
    {
        var model = new FakeModel(Answer(
            """[{"texto":"Buena","citas":["BOE-A-1"]},{"texto":"Mala","citas":["BOE-A-1"]}]"""));
        var verifier = new FakeVerifier { Unsupported = "Mala" };
        var agent = new RegulatoryAnalystAgent(
            model, new FakeEmbeddings(), new FakeIndex([]), verifier, Options.Create(new ModelOptions()),
            Options.Create(new AnalysisOptions { Mode = AnalysisMode.Single }));

        var change = new RegulatoryChange(
            "BOE-A-1", "BOE", "Cambio", new Uri("https://www.boe.es/c"), new DateOnly(2026, 10, 5), "texto");

        var analysis = await agent.AnalyzeAsync(change, default);

        Assert.Equal(["Mala"], analysis.Unsupported.Select(v => v.Claim.Text));
    }

    private sealed class FakeVerifier : ICitationVerifier
    {
        public Dictionary<string, string[]> Calls { get; } = [];
        public string? Unsupported { get; init; }

        public Task<ClaimVerdict> VerifyAsync(Claim claim, IReadOnlyList<TextChunk> cited, CancellationToken ct)
        {
            lock (Calls) Calls[claim.Text] = cited.Select(c => c.Id).ToArray();
            var support = claim.Text == Unsupported ? Support.Unsupported : Support.Supported;
            return Task.FromResult(new ClaimVerdict(claim, support, "fake"));
        }
    }

    private sealed class FakeModel(string answer) : ILanguageModel
    {
        public string Deployment { get; private set; } = "";
        public string Input { get; private set; } = "";

        public Task<string> CompleteAsync(string deployment, string instructions, string input, CancellationToken ct)
        {
            (Deployment, Input) = (deployment, input);
            return Task.FromResult(answer);
        }
    }

    private sealed class FakeEmbeddings : IEmbeddingModel
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f, 0.2f }).ToList());
    }

    private sealed class FakeIndex(IReadOnlyList<NormHit> hits) : IChunkIndex
    {
        public string? ExcludedDocumentId { get; private set; }

        public Task EnsureCreatedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<NormHit>> SearchAsync(
            string text, float[] vector, int top, string? excludeDocumentId, CancellationToken ct)
        {
            ExcludedDocumentId = excludeDocumentId;
            return Task.FromResult(hits);
        }
    }
}
public class NormativeIngestorTests
{
    [Fact]
    public async Task Embeds_in_batches_and_indexes_every_chunk_once()
    {
        var paragraphs = Enumerable.Range(1, 40)
            .Select(i => new SourceParagraph(true, $"Artículo {i}. Algo."))
            .ToList();
        var doc = new SourceDocument("BOE-A-1", "T", "Orden", "H", new DateOnly(2026, 10, 5),
            new Uri("https://www.boe.es/x"), paragraphs);

        var embeddings = new CountingEmbeddings();
        var index = new CollectingIndex();

        var chunks = await new ChunkIngestor(embeddings, index).IngestAsync(doc, default);

        Assert.Equal(40, chunks.Count);
        Assert.Equal(40, index.Indexed.Count);
        Assert.Equal(3, embeddings.Calls); // 16 + 16 + 8
        Assert.Equal(1, index.EnsureCalls);
        Assert.Equal(40, index.Indexed.Select(c => c.Chunk.Id).Distinct().Count());
    }

    private sealed class CountingEmbeddings : IEmbeddingModel
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f }).ToList());
        }
    }

    private sealed class CollectingIndex : IChunkIndex
    {
        public List<IndexedChunk> Indexed { get; } = [];
        public int EnsureCalls { get; private set; }

        public Task EnsureCreatedAsync(CancellationToken ct)
        {
            EnsureCalls++;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct)
        {
            Indexed.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NormHit>> SearchAsync(
            string text, float[] vector, int top, string? excludeDocumentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NormHit>>([]);
    }
}

