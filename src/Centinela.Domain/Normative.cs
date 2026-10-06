namespace Centinela.Domain;

/// <summary>Un párrafo de una norma. <see cref="StartsSection"/> marca el inicio de un artículo o anexo.</summary>
public sealed record SourceParagraph(bool StartsSection, string Text);

/// <summary>Documento oficial completo, ya leído de su fuente y listo para trocear.</summary>
public sealed record SourceDocument(
    string Id,
    string Title,
    string Rank,
    string Department,
    DateOnly PublishedOn,
    Uri Url,
    IReadOnlyList<SourceParagraph> Paragraphs);

/// <summary>
/// Fragmento citable de una norma (normalmente un artículo). Es la unidad que se indexa,
/// se recupera y se cita: cada afirmación de un agente debe apuntar a un <see cref="Id"/>.
/// </summary>
public sealed record TextChunk(
    string Id,
    string DocumentId,
    string DocumentTitle,
    string Label,
    string Text,
    DateOnly PublishedOn,
    Uri Url);
