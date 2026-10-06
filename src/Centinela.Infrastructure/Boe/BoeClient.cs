using System.Text.Json;
using Centinela.Domain;

namespace Centinela.Infrastructure.Boe;

/// <summary>Una disposición del sumario diario del BOE.</summary>
public sealed record BoeEntry(
    string Id, string Title, string Department, string Section, Uri HtmlUrl);

/// <summary>
/// Lee el sumario diario del BOE a través de su API de datos abiertos (sin clave).
/// Documentación: https://www.boe.es/datosabiertos/
/// </summary>
public sealed class BoeClient(HttpClient http)
{
    /// <summary>
    /// Secciones que interesan a una empresa: la I (disposiciones generales: leyes, reales
    /// decretos, órdenes) y la III (otras disposiciones, donde caen muchas resoluciones de la AEAT).
    /// Las secciones de nombramientos, oposiciones o anuncios no cambian obligaciones.
    /// </summary>
    public static readonly string[] CompanySections = ["1", "3"];

    public async Task<IReadOnlyList<BoeEntry>> GetSummaryAsync(
        DateOnly date, IReadOnlyCollection<string>? sections, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"datosabiertos/api/boe/sumario/{date:yyyyMMdd}");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, ct);

        // Los fines de semana y festivos sin edición devuelven 404: no es un error, simplemente no hay BOE.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseSummary(doc.RootElement, sections);
    }

    /// <summary>Descarga el texto completo de una disposición (formato XML oficial).</summary>
    public async Task<SourceDocument> GetDocumentAsync(string id, CancellationToken ct)
    {
        // El identificador acaba en una URL: se valida su forma para no construir peticiones arbitrarias.
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^BOE-[A-Z]-\d{4}-\d+$"))
        {
            throw new ArgumentException($"Identificador del BOE no válido: {id}", nameof(id));
        }

        var xml = await http.GetStringAsync($"diario_boe/xml.php?id={id}", ct);
        return ParseDocument(xml);
    }

    /// <summary>Separado de la red para poder probarlo con un XML de ejemplo.</summary>
    public static SourceDocument ParseDocument(string xml)
    {
        var root = System.Xml.Linq.XDocument.Parse(xml).Root
                   ?? throw new InvalidOperationException("XML del BOE vacío.");

        var meta = root.Element("metadatos")
                   ?? throw new InvalidOperationException("XML del BOE sin metadatos.");

        string Meta(string name) => (string?)meta.Element(name) ?? "";

        var id = Meta("identificador");
        var published = DateOnly.TryParseExact(Meta("fecha_publicacion"), "yyyyMMdd", out var d) ? d : default;

        var paragraphs = new List<SourceParagraph>();
        foreach (var element in root.Element("texto")?.Elements() ?? [])
        {
            var text = Normalize(element.Value);
            if (text.Length == 0) continue;

            // `articulo` abre un artículo y `anexo_num` un anexo; el resto es cuerpo.
            var cls = (string?)element.Attribute("class") ?? "";
            paragraphs.Add(new SourceParagraph(cls is "articulo" or "anexo_num", text));
        }

        return new SourceDocument(
            id, Meta("titulo"), Meta("rango"), Meta("departamento"), published,
            new Uri($"https://www.boe.es/diario_boe/txt.php?id={id}"), paragraphs);
    }

    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>Convierte una entrada del sumario en el cambio que consume el orquestador.</summary>
    public static RegulatoryChange ToChange(BoeEntry entry, DateOnly date) =>
        new(entry.Id, "BOE", entry.Title, entry.HtmlUrl, date,
            // De momento solo se dispone del título y el departamento; el texto completo
            // (url_xml) se descargará en la fase del analista.
            $"Departamento: {entry.Department}\nSección: {entry.Section}");

    /// <summary>Separado de la red para poder probarlo con un JSON de ejemplo.</summary>
    public static IReadOnlyList<BoeEntry> ParseSummary(
        JsonElement root, IReadOnlyCollection<string>? sections)
    {
        var entries = new List<BoeEntry>();
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("sumario", out var summary) ||
            !summary.TryGetProperty("diario", out var diaries))
        {
            return entries;
        }

        foreach (var diary in AsList(diaries))
        foreach (var section in AsList(diary, "seccion"))
        {
            var sectionCode = Text(section, "codigo");
            if (sections is { Count: > 0 } && !sections.Contains(sectionCode)) continue;

            foreach (var department in AsList(section, "departamento"))
            {
                var departmentName = Text(department, "nombre");

                // La profundidad varía según la sección: unos items cuelgan del departamento, otros
                // de un epígrafe, y en la sección I de `texto.epigrafe`. Cada nivel puede ser un
                // objeto suelto o una lista. En vez de enumerar rutas (y perder en silencio las que
                // no conozcamos) se buscan los items a cualquier profundidad.
                foreach (var item in FindItems(department))
                {
                    var id = Text(item, "identificador");
                    var url = Text(item, "url_html");
                    if (id.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var htmlUrl)) continue;

                    entries.Add(new BoeEntry(id, Text(item, "titulo"), departmentName, sectionCode, htmlUrl));
                }
            }
        }

        return entries;
    }

    /// <summary>Recorre el árbol y devuelve cada disposición, identificada por tener `identificador`.</summary>
    private static IEnumerable<JsonElement> FindItems(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                foreach (var found in FindItems(child))
                    yield return found;
                break;

            case JsonValueKind.Object when node.TryGetProperty("identificador", out _):
                // Una disposición es una hoja: no se desciende dentro de ella.
                yield return node;
                break;

            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                foreach (var found in FindItems(property.Value))
                    yield return found;
                break;
        }
    }

    private static IEnumerable<JsonElement> AsList(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value))
        {
            return [];
        }

        return AsList(value);
    }

    private static IEnumerable<JsonElement> AsList(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray(),
            JsonValueKind.Object => [value],
            _ => []
        };

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
