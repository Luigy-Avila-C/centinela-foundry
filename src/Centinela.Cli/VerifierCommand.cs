using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using static Centinela.Cli.CliUtil;

namespace Centinela.Cli;

/// <summary>Comando <c>evaluar</c>: mide el verificador de citas contra el conjunto etiquetado y falla si no cumple los umbrales.</summary>
internal static class VerifierCommand
{
    // centinela evaluar [--split dev|test|all] [--dataset ruta] [--min-recall 0.9] [--max-falsa-alarma 0.2]
    // Mide el verificador de citas contra los casos etiquetados. Termina con código 1 si no cumple los
    // umbrales, de modo que se pueda usar como puerta de calidad.
    public static async Task<int> EvaluateAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var split = "dev"; // por defecto no se toca `test`: se reserva para la medida final
        var path = Path.Combine("evaluaciones", "verificador-citas.json");
        double? minRecall = null, maxFalseAlarm = null;

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var value = args[i + 1];
            switch (args[i])
            {
                case "--split": split = value; break;
                case "--dataset": path = value; break;
                case "--min-recall" when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var r): minRecall = r; break;
                case "--max-falsa-alarma" when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var f): maxFalseAlarm = f; break;
                default: return Fail($"Argumento no reconocido: {args[i]} {value}");
            }
        }

        if (!File.Exists(path)) return Fail($"No existe el conjunto de evaluación: {path}");

        var cases = VerifierEvaluation.LoadDataset(await File.ReadAllTextAsync(path, ct))
            .Where(c => split == "all" || c.Split == split).ToList();
        if (cases.Count == 0) return Fail($"No hay casos en la parte '{split}'.");

        Console.WriteLine($"Evaluando el verificador sobre {cases.Count} casos (parte: {split})…\n");

        var verifier = services.GetRequiredService<ICitationVerifier>();
        var outcomes = await VerifierEvaluation.RunAsync(verifier, cases, parallelism: 4, ct);

        foreach (var o in outcomes.Where(o => o.Case.Expected != o.Actual))
        {
            Console.WriteLine($"✗ {o.Case.Id}: esperado {o.Case.Expected}, obtenido {o.Actual}");
            Console.WriteLine($"    afirmación: {Shorten(o.Case.Claim, 140)}");
            Console.WriteLine($"    motivo del verificador: {o.Reason}\n");
        }

        var report = EvalReport.From(outcomes);
        string Pct(double d) => double.IsNaN(d) ? "n/d" : $"{d:P0}";

        Console.WriteLine("Matriz de confusión (filas = esperado, columnas = obtenido)");
        Console.WriteLine($"{"",-16}{"respaldada",12}{"parcial",12}{"no respald.",12}");
        foreach (var (name, row) in new[] { ("respaldada", Support.Supported), ("parcial", Support.Partial), ("no respaldada", Support.Unsupported) })
        {
            Console.WriteLine($"{name,-16}{report.Confusion[(int)row, 0],12}{report.Confusion[(int)row, 1],12}{report.Confusion[(int)row, 2],12}");
        }

        Console.WriteLine($"\nAcierto exacto ........ {Pct(report.Accuracy)}  ({report.ExactMatches}/{report.Total})");
        Console.WriteLine($"Detección de problemas  {Pct(report.ProblemRecall)}  ({report.Flagged}/{report.ShouldFlag} afirmaciones malas marcadas)");
        Console.WriteLine($"  solo no respaldadas . {Pct(report.UnsupportedRecall)}  ({report.UnsupportedCaught}/{report.UnsupportedExpected})");
        Console.WriteLine($"Falsas alarmas ........ {Pct(report.FalseAlarmRate)}  ({report.FalseAlarms}/{report.ShouldPass} correctas marcadas)");

        var failed = false;
        if (minRecall is { } min && !(report.ProblemRecall >= min))
        {
            Console.Error.WriteLine($"\nFALLO: la detección de problemas ({Pct(report.ProblemRecall)}) no alcanza el mínimo {min:P0}.");
            failed = true;
        }

        if (maxFalseAlarm is { } max && !(report.FalseAlarmRate <= max))
        {
            Console.Error.WriteLine($"\nFALLO: las falsas alarmas ({Pct(report.FalseAlarmRate)}) superan el máximo {max:P0}.");
            failed = true;
        }

        return failed ? 1 : 0;
    }
}
