using System.Text.Json;
using Centinela.Application;
using Centinela.Infrastructure.Persistence;

namespace Centinela.Api;

/// <summary>
/// Modo demostración (<c>Demo__Enabled=true</c>): carga en memoria los casos de <c>datos/demo</c>, que son ejecuciones reales
/// del sistema sobre una empresa FICTICIA y normas públicas del BOE. Permite ver y probar el panel sin cuenta de Azure,
/// sin Cosmos y sin gastar nada. Nada se guarda: al cerrar el proceso se pierde.
/// </summary>
public static class DemoMode
{
    /// <summary>Clave fija de la demo. Solo existe en este modo y el panel la usa solo, para no pedirla.</summary>
    public const string Key = "demo";

    /// <summary>
    /// Carga los casos. Se niega a funcionar si hay un Cosmos configurado: una demo no debe poder escribir (ni parecer que
    /// escribe) en datos reales.
    /// </summary>
    public static async Task<int> SeedAsync(ICaseRepository repository, IConfiguration configuration, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Cosmos:Endpoint"]))
        {
            throw new InvalidOperationException(
                "Demo:Enabled no puede usarse con Cosmos:Endpoint definido: la demo es solo en memoria. Quita una de las dos opciones.");
        }

        var folder = FindFolder(configuration["Demo:Folder"] ?? Path.Combine("datos", "demo"))
                     ?? throw new DirectoryNotFoundException("No se encuentra la carpeta de casos de demostración (datos/demo).");

        var loaded = 0;
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var doc = JsonSerializer.Deserialize<CaseDocument>(await File.ReadAllTextAsync(file, ct), CaseJson.Options)
                      ?? throw new InvalidOperationException($"Caso de demostración vacío: {file}");
            await repository.SaveAsync(doc.ToCase(), ct);
            loaded++;
        }

        return loaded;
    }

    // Se busca hacia arriba desde el directorio actual y desde el del ejecutable, para que funcione se lance desde donde se lance.
    private static string? FindFolder(string relative)
    {
        if (Path.IsPathRooted(relative)) return Directory.Exists(relative) ? relative : null;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (Directory.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
