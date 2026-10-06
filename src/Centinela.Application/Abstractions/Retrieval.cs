using Centinela.Domain;

namespace Centinela.Application;

/// <summary>Convierte texto en vectores para la búsqueda semántica.</summary>
public interface IEmbeddingModel
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>Un fragmento junto con su vector, listo para indexar.</summary>
public sealed record IndexedChunk(TextChunk Chunk, float[] Vector);

/// <summary>Un fragmento recuperado por una búsqueda, con su puntuación.</summary>
public sealed record NormHit(TextChunk Chunk, double Score);

/// <summary>Índice de la normativa (Azure AI Search en producción).</summary>
public interface IChunkIndex
{
    /// <summary>Crea el índice si no existe. Es idempotente.</summary>
    Task EnsureCreatedAsync(CancellationToken ct);

    /// <summary>Inserta o actualiza fragmentos; reindexar una norma no duplica nada.</summary>
    Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct);

    /// <summary>Búsqueda híbrida (texto + vector).</summary>
    /// <param name="excludeDocumentId">Documento a omitir, normalmente el que se está analizando.</param>
    Task<IReadOnlyList<NormHit>> SearchAsync(
        string text, float[] vector, int top, string? excludeDocumentId, CancellationToken ct);
}

/// <summary>Trocea una norma, calcula sus vectores y la deja indexada.</summary>
public sealed class ChunkIngestor(IEmbeddingModel embeddings, IChunkIndex index)
{
    // Un lote por llamada: menos viajes a la red sin acercarse al límite de entrada del modelo.
    private const int BatchSize = 16;

    /// <returns>Los fragmentos indexados.</returns>
    public async Task<IReadOnlyList<TextChunk>> IngestAsync(SourceDocument document, CancellationToken ct)
    {
        var chunks = DocumentChunker.Chunk(document);
        if (chunks.Count == 0) return chunks;

        await index.EnsureCreatedAsync(ct);

        foreach (var batch in chunks.Chunk(BatchSize))
        {
            // Se incluye el título: sin él, un artículo suelto pierde de qué norma habla.
            var inputs = batch.Select(c => $"{c.DocumentTitle}\n{c.Label}\n{c.Text}").ToList();
            var vectors = await embeddings.EmbedAsync(inputs, ct);

            await index.UpsertAsync(
                batch.Select((chunk, i) => new IndexedChunk(chunk, vectors[i])).ToList(), ct);
        }

        return chunks;
    }
}
