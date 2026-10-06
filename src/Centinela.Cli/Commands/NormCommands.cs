using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using static Centinela.Cli.CliUtil;

namespace Centinela.Cli;

/// <summary>Comandos sobre normas del BOE: <c>cribar</c>, <c>indexar</c>, <c>analizar</c> y <c>ver</c>.</summary>
internal static class NormCommands
{
    // centinela ver <BOE-A-...> [--desde N] [--hasta N] [--chars N]
    // Vuelca los fragmentos de una norma tal y como los ve el sistema. No llama a ningún modelo.
    public static async Task<int> ViewAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        int from = 1, to = int.MaxValue, chars = 600;
        for (var i = 1; i < args.Length - 1; i += 2)
        {
            var ok = int.TryParse(args[i + 1], out var v);
            if (args[i] == "--desde" && ok) from = v;
            else if (args[i] == "--hasta" && ok) to = v;
            else if (args[i] == "--chars" && ok) chars = v;
            else return Fail($"Argumento no reconocido: {args[i]}");
        }

        var document = await services.GetRequiredService<BoeClient>().GetDocumentAsync(args[0], ct);
        var chunks = DocumentChunker.Chunk(document);

        Console.WriteLine($"{document.Title}\n{chunks.Count} fragmentos\n");
        for (var n = Math.Max(from, 1); n <= Math.Min(to, chunks.Count); n++)
        {
            var chunk = chunks[n - 1];
            Console.WriteLine($"=== [{chunk.Id}] {chunk.Label}  ({chunk.Text.Length} car.)");
            Console.WriteLine(chunk.Text.Length <= chars ? chunk.Text : chunk.Text[..chars] + " […]");
            Console.WriteLine();
        }

        return 0;
    }

    public static async Task<int> ScreenAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(DateTime.Today);
        var max = 15; // tope por ejecución: cada disposición es una llamada de pago al modelo

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--max" && i + 1 < args.Length && int.TryParse(args[++i], out var n)) max = n;
            else if (DateOnly.TryParse(args[i], out var d)) date = d;
            else return Fail($"Argumento no reconocido: {args[i]}");
        }

        var boe = services.GetRequiredService<BoeClient>();
        var screening = services.GetRequiredService<ScreeningAgent>();

        var entries = await boe.GetSummaryAsync(date, BoeClient.CompanySections, ct);
        if (entries.Count == 0)
        {
            Console.WriteLine($"No hay disposiciones en las secciones vigiladas para {date:yyyy-MM-dd}.");
            return 0;
        }

        var analysed = Math.Min(max, entries.Count);
        Console.WriteLine($"{entries.Count} disposiciones en las secciones I y III del {date:yyyy-MM-dd}; se analizan {analysed}.\n");

        var relevant = 0;
        foreach (var entry in entries.Take(max))
        {
            var result = await screening.ClassifyAsync(BoeClient.ToChange(entry, date), ct);
            if (result.Relevant) relevant++;

            Console.WriteLine($"{(result.Relevant ? "[SÍ]" : "[no]")} {entry.Id}");
            Console.WriteLine($"     {Shorten(entry.Title, 150)}");
            Console.WriteLine($"     → {result.Reason}\n");
        }

        Console.WriteLine($"Relevantes: {relevant} de {analysed}");
        return 0;
    }

    public static async Task<int> IndexAsync(IServiceProvider services, string[] ids, CancellationToken ct)
    {
        var boe = services.GetRequiredService<BoeClient>();
        var ingestor = services.GetRequiredService<ChunkIngestor>();

        foreach (var id in ids)
        {
            var document = await boe.GetDocumentAsync(id, ct);
            var chunks = await ingestor.IngestAsync(document, ct);
            Console.WriteLine($"{id}: {chunks.Count} fragmentos indexados — {Shorten(document.Title, 100)}");
        }

        return 0;
    }

    public static async Task<int> AnalyzeAsync(IServiceProvider services, string id, CancellationToken ct)
    {
        var boe = services.GetRequiredService<BoeClient>();
        var analyst = services.GetRequiredService<IRegulatoryAnalystAgent>();

        var document = await boe.GetDocumentAsync(id, ct);
        var chunks = DocumentChunker.Chunk(document);
        var fullText = string.Join("\n", document.Paragraphs.Select(p => p.Text));

        Console.WriteLine($"{document.Title}\n{chunks.Count} fragmentos, {fullText.Length:N0} caracteres.\n");

        var change = new RegulatoryChange(
            document.Id, "BOE", document.Title, document.Url, document.PublishedOn, fullText, chunks);

        var analysis = await analyst.AnalyzeAsync(change, ct);

        Console.WriteLine("RESUMEN\n" + analysis.Summary + "\n");

        Console.WriteLine("AFIRMACIONES (cada una con su cita y el veredicto del verificador)");
        foreach (var verdict in analysis.Verdicts ?? [])
        {
            var mark = verdict.Support switch { Support.Supported => "✓", Support.Partial => "~", _ => "✗" };
            Console.WriteLine($"  {mark} {verdict.Claim.Text}");
            Console.WriteLine($"      citas: {string.Join(", ", verdict.Claim.Citations)}");
            if (verdict.Support != Support.Supported) Console.WriteLine($"      {verdict.Support}: {verdict.Reason}");
        }

        var all = analysis.Verdicts ?? [];
        Console.WriteLine($"\nRespaldadas: {all.Count(v => v.Support == Support.Supported)}  " +
                          $"Parciales: {all.Count(v => v.Support == Support.Partial)}  " +
                          $"No respaldadas: {all.Count(v => v.Support == Support.Unsupported)}  (de {all.Count})");

        return analysis.Unsupported.Any() ? 2 : 0;
    }
}
