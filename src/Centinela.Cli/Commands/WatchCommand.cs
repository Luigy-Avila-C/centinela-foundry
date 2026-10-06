using Centinela.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centinela.Cli;

/// <summary>
/// <c>centinela vigilar [yyyy-MM-dd ...] [--simular] [--max-casos N]</c>: una pasada del vigilante, a mano. Con
/// <c>--simular</c> criba pero no descarga ni guarda nada: sirve para ver qué haría antes de dejarle gastar.
/// </summary>
internal static class WatchCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var simulate = false;
        var dates = new List<DateOnly>();
        var options = services.GetRequiredService<IOptions<WatcherOptions>>().Value;
        var perRun = options.MaxNewCasesPerRun;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--simular") simulate = true;
            else if (args[i] == "--max-casos" && i + 1 < args.Length && int.TryParse(args[++i], out var n) && n >= 0) perRun = n;
            else if (DateOnly.TryParse(args[i], out var d)) dates.Add(d);
            else return Fail($"Argumento no reconocido: {args[i]}");
        }

        // Se ajusta una copia: el tope por pasada de la línea de comandos no toca la configuración del servicio.
        var local = new WatcherOptions
        {
            Enabled = true, IntervalMinutes = options.IntervalMinutes, LookbackDays = options.LookbackDays,
            MaxNewCasesPerRun = perRun, MaxNewCasesPerDay = options.MaxNewCasesPerDay,
            MaxScreeningsPerRun = options.MaxScreeningsPerRun, MaxAttempts = options.MaxAttempts,
        };
        var watcher = new RegulatoryWatcher(
            services.GetRequiredService<IRegulatorySource>(),
            services.GetRequiredService<ISecurityGuardAgent>(),
            services.GetRequiredService<IScreeningAgent>(),
            services.GetRequiredService<ComplianceWorkflow>(),
            services.GetRequiredService<ICaseRepository>(),
            Options.Create(local),
            services.GetRequiredService<ILogger<RegulatoryWatcher>>());

        if (dates.Count == 0) dates.AddRange(watcher.DefaultDates());

        Console.WriteLine($"Vigilante · {(simulate ? "SIMULACIÓN" : "REAL")} · días {string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd")))} · " +
                          $"hasta {perRun} caso(s) caros por pasada y {local.MaxNewCasesPerDay} por día\n");

        var report = await watcher.RunAsync(dates, simulate, ct);

        foreach (var item in report.Items.Where(i => i.Outcome is not (WatchOutcome.AlreadyKnown or WatchOutcome.Irrelevant or WatchOutcome.NotScreened)))
        {
            Console.WriteLine($"[{item.Outcome}] {item.SourceId}  {Shorten(item.Title, 110)}\n      {Shorten(item.Detail, 160)}");
        }

        Console.WriteLine($"\n{report.Summary()}");
        Console.WriteLine("\nConsumo de la pasada (cribado + casos):\n" +
                          UsageReport.Format(report.Usage ?? [], services.GetRequiredService<IOptions<PricingOptions>>().Value));
        return report.Count(WatchOutcome.Error) > 0 ? 2 : 0;
    }

    private static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
