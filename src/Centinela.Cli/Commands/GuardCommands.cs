using System.Globalization;
using Centinela.Application;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Cli;

/// <summary>
/// Comandos del guardián de seguridad. <c>evaluar-guardian</c> mide si el guardián detecta los ataques del conjunto etiquetado y
/// si deja pasar la normativa real; <c>inspeccionar</c> pasa el guardián por un fichero de texto cualquiera.
/// </summary>
internal static class GuardCommands
{
    private const string DefaultDataset = "evaluaciones/guardian-inyecciones.json";

    public static async Task<int> EvaluateAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var split = "dev";
        string? modelName = null;
        var dataset = DefaultDataset;
        var verbose = false;

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var v = args[i + 1];
            switch (args[i])
            {
                case "--parte" when v is "dev" or "test" or "all": split = v; break;
                case "--modelo": modelName = v; break;
                case "--etiquetas": dataset = v; break;
                case "--detalle" when v is "si" or "no": verbose = v == "si"; break;
                default: return Fail($"Argumento no reconocido: {args[i]} {v}");
            }
        }

        if (!File.Exists(dataset)) return Fail($"No existe el conjunto de ataques: {dataset}");

        var (attacks, hard) = GuardEvaluation.LoadDataset(await File.ReadAllTextAsync(dataset, ct));

        // La norma real: es pública y su descarga no cuesta nada. Sus secciones son los negativos y los portadores.
        var boe = services.GetRequiredService<BoeClient>();
        var norm = await boe.GetDocumentAsync("BOE-A-2026-20587", ct);
        var sections = DocumentChunker.Chunk(norm).Where(c => c.Text.Length is >= 200 and <= 3500).ToList();

        var samples = GuardEvaluation.BuildSamples(sections, attacks, hard)
            .Where(s => split == "all" || s.Split == split).ToList();

        var models = services.GetRequiredService<IOptions<ModelOptions>>().Value;
        var guardModel = modelName ?? models.Fast;
        var guard = new SecurityGuardAgent(
            services.GetRequiredService<ILanguageModel>(),
            Options.Create(models),
            Options.Create(new GuardOptions { Model = guardModel }));

        Console.WriteLine($"Evaluando el guardián · parte «{split}» · modelo {guardModel} · {samples.Count} muestras " +
                          $"({samples.Count(s => s.IsAttack)} ataques, {samples.Count(s => !s.IsAttack)} negativos)\n");

        var results = await GuardRunner.RunAsync(samples, guard, ct);

        var r = GuardEvaluation.Evaluate(results);
        string Pct(int a, int b) => b == 0 ? "n/d" : ((double)a / b).ToString("P0", CultureInfo.InvariantCulture);

        Console.WriteLine($"Ataques detectados (guardián completo) .. {Pct(r.AttacksDetected, r.AttacksTotal)}  ({r.AttacksDetected}/{r.AttacksTotal})");
        Console.WriteLine($"   solo las reglas del código ........... {r.CodeDetected}/{r.AttacksTotal}   solo el modelo ........ {r.ModelDetected}/{r.AttacksTotal}   filtro de la plataforma {r.PlatformDetected}/{r.AttacksTotal}");
        Console.WriteLine($"   cazados únicamente por el código {r.DetectedByCodeOnly} · únicamente por el modelo {r.DetectedByModelOnly} · por ambos {r.DetectedByBoth}");
        Console.WriteLine($"Secciones reales de la norma bloqueadas .. {r.RealFlagged.Count}/{r.RealTotal}");
        Console.WriteLine($"Negativos difíciles bloqueados ........... {r.HardFlagged.Count}/{r.HardTotal}");
        Console.WriteLine($"Hallazgos del modelo descartados por no verificables: {r.DiscardedModelFindings}");

        if (r.Missed.Count > 0)
        {
            Console.WriteLine("\nAtaques que se escaparon:");
            foreach (var m in r.Missed) Console.WriteLine($"  - {m}");
        }

        if (r.RealFlagged.Count + r.HardFlagged.Count > 0)
        {
            Console.WriteLine("\nFalsos positivos (texto legítimo bloqueado):");
            foreach (var f in r.RealFlagged.Concat(r.HardFlagged)) Console.WriteLine($"  - {f}");
        }

        if (verbose)
        {
            Console.WriteLine("\nDetalle de ataques:");
            foreach (var (s, scan) in results.Where(x => x.Sample.IsAttack).OrderBy(x => x.Sample.Id, StringComparer.Ordinal))
            {
                Console.WriteLine($"\n  {s.Id} [{s.Technique}] → {(scan.IsSafe ? "NO DETECTADO" : "detectado")}");
                foreach (var f in scan.Findings) Console.WriteLine($"      {f.Origin}/{f.Kind}: «{Shorten(f.Quote, 90)}»");
            }
        }

        return 0;
    }

    /// <summary>Pasa el guardián por un fichero de texto y dice qué encuentra.</summary>
    public static async Task<int> InspectAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var path = args[0];
        if (!File.Exists(path)) return Fail($"No existe el fichero: {path}");

        var guard = services.GetRequiredService<SecurityGuardAgent>();
        var scan = await guard.ScanAsync(await File.ReadAllTextAsync(path, ct), ct);

        Console.WriteLine(scan.IsSafe ? "Sin indicios de inyección." : $"BLOQUEADO: {scan.Findings.Count} indicio(s).");
        foreach (var f in scan.Findings) Console.WriteLine($"  {f.Origin}/{f.Kind}: «{Shorten(f.Quote, 140)}»  {f.Description}");
        return scan.IsSafe ? 0 : 2;
    }

    private static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
