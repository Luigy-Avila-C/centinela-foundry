using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Centinela.Application;
using Centinela.Domain;
using Microsoft.Extensions.Options;

namespace Centinela.Infrastructure.Search;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Endpoint del servicio (https://&lt;servicio&gt;.search.windows.net). No es un secreto.</summary>
    public string Endpoint { get; set; } = "";

    public string IndexName { get; set; } = "normativa";

    /// <summary>Índice de los documentos internos de la empresa (separado del de la normativa).</summary>
    public string CompanyIndexName { get; set; } = "empresa";

    /// <summary>Debe coincidir con las dimensiones del modelo de embeddings (1536 en text-embedding-3-small).</summary>
    public int Dimensions { get; set; } = 1536;
}

/// <summary>Índice de normativa sobre Azure AI Search con búsqueda híbrida (texto + vector).</summary>
public sealed class AzureSearchChunkIndex : IChunkIndex
{
    private const string VectorProfile = "perfil-vectorial";
    private const string VectorAlgorithm = "hnsw";

    private readonly SearchOptions _options;
    private readonly SearchIndexClient _indexClient;
    private readonly Azure.Search.Documents.SearchClient _client;

    public AzureSearchChunkIndex(IOptions<SearchOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "Falta Search:Endpoint. Defínelo en la configuración o con la variable de entorno Search__Endpoint.");
        }

        // Solo Microsoft Entra ID: el servicio tiene las claves de API desactivadas.
        var credential = new DefaultAzureCredential();
        var endpoint = new Uri(_options.Endpoint);
        _indexClient = new SearchIndexClient(endpoint, credential);
        _client = _indexClient.GetSearchClient(_options.IndexName);
    }

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        var index = new SearchIndex(_options.IndexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
                new SearchableField("documentTitle") { AnalyzerName = LexicalAnalyzerName.EsMicrosoft },
                new SearchableField("label") { AnalyzerName = LexicalAnalyzerName.EsMicrosoft },
                new SearchableField("text") { AnalyzerName = LexicalAnalyzerName.EsMicrosoft },
                new SimpleField("publishedOn", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SimpleField("url", SearchFieldDataType.String),
                new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = _options.Dimensions,
                    VectorSearchProfileName = VectorProfile,
                },
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration(VectorAlgorithm) },
                Profiles = { new VectorSearchProfile(VectorProfile, VectorAlgorithm) },
            },
        };

        // CreateOrUpdate es idempotente: ejecutarlo en cada arranque no pierde datos.
        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
    }

    public async Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct)
    {
        var documents = chunks.Select(c => new SearchDocument
        {
            ["id"] = c.Chunk.Id,
            ["documentId"] = c.Chunk.DocumentId,
            ["documentTitle"] = c.Chunk.DocumentTitle,
            ["label"] = c.Chunk.Label,
            ["text"] = c.Chunk.Text,
            ["publishedOn"] = new DateTimeOffset(c.Chunk.PublishedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            ["url"] = c.Chunk.Url.ToString(),
            ["embedding"] = c.Vector,
        }).ToList();

        var response = await _client.MergeOrUploadDocumentsAsync(documents, cancellationToken: ct);

        // Un fragmento que no se indexó y se da por bueno es una laguna silenciosa en el RAG.
        var failed = response.Value.Results.Where(r => !r.Succeeded).Select(r => r.Key).ToList();
        if (failed.Count > 0)
        {
            throw new InvalidOperationException($"No se pudieron indexar: {string.Join(", ", failed)}.");
        }
    }

    public async Task<IReadOnlyList<NormHit>> SearchAsync(
        string text, float[] vector, int top, string? excludeDocumentId, CancellationToken ct)
    {
        var options = new Azure.Search.Documents.SearchOptions
        {
            Size = top,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector) { KNearestNeighborsCount = top * 3, Fields = { "embedding" } },
                },
            },
        };

        if (excludeDocumentId is not null)
        {
            options.Filter = $"documentId ne '{excludeDocumentId.Replace("'", "''")}'";
        }

        // `SearchText` + vector = búsqueda híbrida: léxica (artículos y términos exactos) más semántica.
        var response = await _client.SearchAsync<SearchDocument>(text, options, ct);

        var hits = new List<NormHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var d = result.Document;
            hits.Add(new NormHit(
                new TextChunk(
                    (string)d["id"], (string)d["documentId"], (string)d["documentTitle"],
                    (string)d["label"], (string)d["text"],
                    ParseDate(d["publishedOn"]),
                    new Uri((string)d["url"])),
                result.Score ?? 0));
        }

        return hits;
    }

    /// <summary>
    /// Un <see cref="SearchDocument"/> entrega las fechas como texto ISO 8601 o como
    /// <see cref="DateTimeOffset"/> según cómo se lea; se aceptan las dos formas.
    /// </summary>
    public static DateOnly ParseDate(object? value) => value switch
    {
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
        string s => DateOnly.FromDateTime(
            DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime),
        _ => throw new InvalidOperationException($"Fecha del índice no interpretable: {value ?? "null"}"),
    };
}
