using System.Text;
using Centinela.Domain;

namespace Centinela.Application;

/// <summary>
/// Trocea una norma en fragmentos citables. Se corta por artículo (no por un tamaño fijo) para
/// que cada fragmento signifique algo por sí solo y se pueda citar como "Artículo 3".
/// </summary>
public static class DocumentChunker
{
    /// <summary>Tope de un fragmento; un artículo más largo se parte por párrafos.</summary>
    public const int DefaultMaxChars = 6000;

    public static IReadOnlyList<TextChunk> Chunk(SourceDocument document, int maxChars = DefaultMaxChars)
    {
        var groups = new List<(string Label, List<string> Paragraphs)>();

        foreach (var paragraph in document.Paragraphs)
        {
            var text = paragraph.Text.Trim();
            if (text.Length == 0) continue;

            if (paragraph.StartsSection || groups.Count == 0)
            {
                // El texto anterior al primer artículo (exposición de motivos, etc.) va a "Preámbulo".
                var label = paragraph.StartsSection ? Shorten(text, 120) : "Preámbulo";
                groups.Add((label, [text]));
            }
            else
            {
                groups[^1].Paragraphs.Add(text);
            }
        }

        var chunks = new List<TextChunk>();
        foreach (var (label, paragraphs) in groups)
        {
            foreach (var part in SplitToFit(paragraphs, maxChars))
            {
                // Las claves de Azure AI Search solo admiten letras, dígitos, "_", "-" y "=".
                var id = $"{document.Id}_{chunks.Count + 1:000}";
                chunks.Add(new TextChunk(
                    id, document.Id, document.Title, label, part,
                    document.PublishedOn, document.Url));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitToFit(List<string> paragraphs, int maxChars)
    {
        var buffer = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            // Un único párrafo gigante (p. ej. una tabla) se corta en seco como último recurso.
            var pieces = paragraph.Length <= maxChars
                ? [paragraph]
                : Enumerable.Range(0, (paragraph.Length + maxChars - 1) / maxChars)
                    .Select(i => paragraph.Substring(i * maxChars, Math.Min(maxChars, paragraph.Length - i * maxChars)))
                    .ToArray();

            foreach (var piece in pieces)
            {
                if (buffer.Length > 0 && buffer.Length + piece.Length + 1 > maxChars)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                if (buffer.Length > 0) buffer.Append('\n');
                buffer.Append(piece);
            }
        }

        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private static string Shorten(string text, int length)
    {
        var firstLine = text.Split('\n')[0];
        return firstLine.Length <= length ? firstLine : firstLine[..length] + "…";
    }
}
