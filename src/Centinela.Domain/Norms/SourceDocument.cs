namespace Centinela.Domain;

/// <summary>Documento oficial completo, ya leído de su fuente y listo para trocear.</summary>
public sealed record SourceDocument(
    string Id,
    string Title,
    string Rank,
    string Department,
    DateOnly PublishedOn,
    Uri Url,
    IReadOnlyList<SourceParagraph> Paragraphs);
