using Centinela.Application;
using Microsoft.Extensions.Options;

namespace Centinela.Worker;

/// <summary>
/// Ejecuta el vigilante cada <see cref="WatcherOptions.IntervalMinutes"/> minutos. Si <see cref="WatcherOptions.Enabled"/>
/// es falso no hace nada: cada caso gasta modelos de pago y un servicio no debe empezar a gastar sin que se le pida.
/// Un fallo en una pasada se registra y se espera a la siguiente; no tumba el servicio.
/// </summary>
public sealed class WatcherService(
    IServiceScopeFactory scopes, IOptions<WatcherOptions> options, ILogger<WatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            logger.LogWarning("El vigilante está DESACTIVADO (Watcher:Enabled=false): no se consulta ninguna fuente ni se gasta nada. " +
                              "Actívalo con la variable de entorno Watcher__Enabled=true.");
            return;
        }

        logger.LogInformation(
            "Vigilante activo: cada {Minutes} min, hasta {Run} caso(s) por pasada y {Day} por día.",
            o.IntervalMinutes, o.MaxNewCasesPerRun, o.MaxNewCasesPerDay);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, o.IntervalMinutes)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var watcher = scope.ServiceProvider.GetRequiredService<RegulatoryWatcher>();
                var report = await watcher.RunAsync(watcher.DefaultDates(), simulate: false, stoppingToken);
                var pricing = scope.ServiceProvider.GetRequiredService<IOptions<PricingOptions>>().Value;
                var cost = CostEstimator.Estimate(report.Usage ?? [], pricing);
                logger.LogInformation("Pasada del vigilante: {Summary} · tokens {In:N0} entrada / {Out:N0} salida · ≈ {Usd:F2} USD estimados",
                    report.Summary(), (report.Usage ?? []).Sum(u => u.InputTokens), (report.Usage ?? []).Sum(u => u.OutputTokens), cost.Usd);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Fallo en la pasada del vigilante; se reintenta en la siguiente.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
