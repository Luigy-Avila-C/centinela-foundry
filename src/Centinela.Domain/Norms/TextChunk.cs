namespace Centinela.Domain;

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
