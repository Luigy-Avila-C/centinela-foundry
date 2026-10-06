using System.Text.Json;
using Centinela.Application;
using Centinela.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Centinela.Cli;

/// <summary>
/// <c>centinela exportar-caso &lt;id&gt; &lt;fichero.json&gt;</c>: guarda un caso tal y como se persiste en Cosmos DB. Sirve para
/// preparar los casos de demostración (<c>datos/demo</c>) a partir de ejecuciones reales sobre datos ficticios.
/// </summary>
internal static class ExportCaseCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        if (args.Length < 2 || !Guid.TryParse(args[0], out var id))
        {
            Console.Error.WriteLine("Uso: centinela exportar-caso <id-del-caso> <fichero.json>");
            return 1;
        }

        var c = await services.GetRequiredService<ICaseRepository>().GetAsync(id, ct);
        if (c is null)
        {
            Console.Error.WriteLine($"No existe el caso {id}.");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        var doc = CaseDocument.From(c, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(doc, new JsonSerializerOptions(CaseJson.Options) { WriteIndented = true }), ct);
        Console.WriteLine($"Caso {id} ({c.Status}) guardado en {args[1]} ({new FileInfo(args[1]).Length / 1024:N0} KB).");
        return 0;
    }
}
