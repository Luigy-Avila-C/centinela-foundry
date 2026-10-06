namespace Centinela.Domain;

/// <summary>Un párrafo de una norma. <see cref="StartsSection"/> marca el inicio de un artículo o anexo.</summary>
public sealed record SourceParagraph(bool StartsSection, string Text);
