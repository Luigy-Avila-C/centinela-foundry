using Centinela.Domain;

namespace Centinela.Application;

/// <summary>
/// Lee un documento interno de la empresa escrito en Markdown y lo convierte en un <see cref="SourceDocument"/>
/// para trocearlo y editarlo igual que una norma. Cada encabezado de segundo nivel (<c>##</c>) abre una sección;
/// el título (<c>#</c>) es el título del documento y las líneas <c>&gt;</c> (avisos) no forman parte del contenido.
/// </summary>
public static class CompanyDocumentLoader
{
    /// <param name="documentId">Identificador estable; normalmente el nombre del fichero sin extensión.</param>
    public static SourceDocument Parse(string markdown, string documentId, Uri location, DateOnly version)
    {
        var title = documentId;
        var paragraphs = new List<SourceParagraph>();
        var buffer = new List<string>();

        void Flush()
        {
            var text = string.Join(" ", buffer).Trim();
            if (text.Length > 0) paragraphs.Add(new SourceParagraph(false, text));
            buffer.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                // El texto del encabezado es la etiqueta de la sección y abre un fragmento nuevo.
                paragraphs.Add(new SourceParagraph(true, line.TrimStart('#').Trim()));
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                title = line[2..].Trim();
            }
            else if (line.StartsWith('>'))
            {
                // Aviso o nota de documento; no es contenido de la empresa.
                Flush();
            }
            else if (line.Length == 0)
            {
                Flush();
            }
            else
            {
                buffer.Add(line.Trim());
            }
        }

        Flush();

        return new SourceDocument(documentId, title, "Documento interno", "Empresa", version, location, paragraphs);
    }
}
