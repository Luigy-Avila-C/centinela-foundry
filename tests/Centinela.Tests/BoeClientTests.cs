using System.Text.Json;
using Centinela.Infrastructure.Boe;

namespace Centinela.Tests;

public class BoeClientTests
{
    // Reproduce las formas reales de la API: los items cuelgan del departamento o de un epígrafe,
    // y cada nivel puede ser un objeto suelto o una lista.
    private const string Sample = """
        {
          "status": {"code": "200"},
          "data": { "sumario": { "diario": [ { "seccion": [
            { "codigo": "1", "nombre": "I. Disposiciones generales",
              "departamento": { "codigo": "1", "nombre": "MINISTERIO DE HACIENDA",
                "texto": { "epigrafe": [ { "nombre": "Procesos de facturación",
                  "item": { "identificador": "BOE-A-1", "titulo": "Orden sobre facturación", "url_html": "https://www.boe.es/diario_boe/txt.php?id=BOE-A-1" } },
                  { "nombre": "Otros", "item": [
                  { "identificador": "BOE-A-2", "titulo": "Otra orden", "url_html": "https://www.boe.es/diario_boe/txt.php?id=BOE-A-2" } ] }
                ] },
                "item": { "identificador": "BOE-A-3", "titulo": "Item suelto", "url_html": "https://www.boe.es/diario_boe/txt.php?id=BOE-A-3" } } },
            { "codigo": "2A", "nombre": "II. Nombramientos",
              "departamento": [ { "codigo": "9", "nombre": "CONSEJO",
                "epigrafe": { "nombre": "Cargos",
                  "item": { "identificador": "BOE-A-4", "titulo": "Nombramiento", "url_html": "https://www.boe.es/diario_boe/txt.php?id=BOE-A-4" } } } ] },
            { "codigo": "3", "nombre": "III. Otras disposiciones",
              "departamento": [ { "codigo": "5", "nombre": "AEAT",
                "epigrafe": [ { "nombre": "Resoluciones", "item": [
                  { "identificador": "BOE-A-5", "titulo": "Resolución AEAT", "url_html": "https://www.boe.es/diario_boe/txt.php?id=BOE-A-5" } ] } ] } ] }
          ] } ] } }
        }
        """;

    [Fact]
    public void Filters_by_section_and_reads_items_under_epigrafe_as_list_or_object()
    {
        using var doc = JsonDocument.Parse(Sample);

        var entries = BoeClient.ParseSummary(doc.RootElement, BoeClient.CompanySections);

        // La sección 2A (nombramientos) queda fuera. BOE-A-1 está en la forma real de la sección I
        // (texto.epigrafe[].item); perderlo en silencio dejaría fuera normas enteras sin avisar (regresión cubierta aquí).
        Assert.Equal(["BOE-A-1", "BOE-A-2", "BOE-A-3", "BOE-A-5"], entries.Select(e => e.Id).Order());
        Assert.All(entries, e => Assert.Contains(e.Section, BoeClient.CompanySections));
    }

    [Fact]
    public void Without_a_filter_returns_every_section()
    {
        using var doc = JsonDocument.Parse(Sample);

        var entries = BoeClient.ParseSummary(doc.RootElement, sections: null);

        Assert.Contains(entries, e => e.Id == "BOE-A-4");
    }

    [Fact]
    public void Unexpected_shape_returns_empty_instead_of_throwing()
    {
        using var doc = JsonDocument.Parse("""{"status": {"code": "200"}}""");

        Assert.Empty(BoeClient.ParseSummary(doc.RootElement, null));
    }
}
