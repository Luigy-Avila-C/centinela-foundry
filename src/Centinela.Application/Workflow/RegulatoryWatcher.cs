using Centinela.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centinela.Application;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    /// <summary>
    /// El Worker NO hace nada mientras no se active expresamente: cada caso consume modelos de pago, y un servicio que
    /// arranca solo y empieza a gastar es justo la sorpresa que se quiere evitar.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Cada cuántos minutos repasa las fuentes.</summary>
    public int IntervalMinutes { get; set; } = 180;

    /// <summary>Cuántos días hacia atrás repasa además de hoy (el BOE puede publicar con retraso).</summary>
    public int LookbackDays { get; set; } = 1;

    /// <summary>Tope de casos CAROS (análisis completo + redacción + auditoría) que se abren en una pasada.</summary>
    public int MaxNewCasesPerRun { get; set; } = 2;

    /// <summary>Tope de casos caros por día (UTC), sumando todas las pasadas.</summary>
    public int MaxNewCasesPerDay { get; set; } = 3;

    /// <summary>Tope de publicaciones que se criban (barato, pero no gratis) en una pasada.</summary>
    public int MaxScreeningsPerRun { get; set; } = 100;

    /// <summary>Veces que se reintenta una publicación cuyo caso falló. Pasado el tope se deja a una persona.</summary>
    public int MaxAttempts { get; set; } = 2;
}

/// <summary>Qué pasó con una publicación en una pasada.</summary>
public enum WatchOutcome
{
    AlreadyKnown,
    RetriesExhausted,
    NotScreened,
    BlockedByGuard,
    Irrelevant,
    NoBudget,
    WouldProcess,
    Processed,
    Error,
}

public sealed record WatchItem(string SourceId, string Title, WatchOutcome Outcome, string Detail);

public sealed record WatchReport(
    IReadOnlyList<DateOnly> Dates, int Entries, IReadOnlyList<WatchItem> Items, bool Simulated, IReadOnlyList<ModelUsage>? Usage = null)
{
    public int Count(WatchOutcome o) => Items.Count(i => i.Outcome == o);

    /// <summary>Una línea con el balance de la pasada.</summary>
    public string Summary() =>
        $"{Entries} publicación(es) en {Dates.Count} día(s){(Simulated ? " [SIMULACIÓN: no se guarda nada]" : "")} · " +
        $"ya conocidas {Count(WatchOutcome.AlreadyKnown) + Count(WatchOutcome.RetriesExhausted)} · irrelevantes {Count(WatchOutcome.Irrelevant)} · " +
        $"bloqueadas por el guardián {Count(WatchOutcome.BlockedByGuard)} · " +
        $"{(Simulated ? "se abrirían" : "casos abiertos")} {Count(WatchOutcome.Processed) + Count(WatchOutcome.WouldProcess)} · " +
        $"sin presupuesto {Count(WatchOutcome.NoBudget)} · sin cribar {Count(WatchOutcome.NotScreened)} · errores {Count(WatchOutcome.Error)}";
}

/// <summary>
/// El vigilante. Repasa las fuentes oficiales y abre casos de forma autónoma, pero solo hasta dejarlos
/// <b>pendientes de aprobación humana</b>: nunca aprueba ni aplica nada (eso lo impone <see cref="ComplianceCase"/>).
/// Embudo, de barato a caro, para gastar solo donde hace falta:
/// <list type="number">
///   <item>sumario del día (gratis) y descarte de lo que ya se conoce;</item>
///   <item>guardián + cribado sobre título y departamento (un par de llamadas a un modelo pequeño);</item>
///   <item>solo lo relevante se descarga entero y pasa por el flujo completo, con topes por pasada y por día.</item>
/// </list>
/// Lo relevante que no cabe en el presupuesto NO se guarda: se vuelve a encontrar en la siguiente pasada.
/// </summary>
public sealed class RegulatoryWatcher(
    IRegulatorySource source,
    ISecurityGuardAgent guard,
    IScreeningAgent screening,
    ComplianceWorkflow workflow,
    ICaseRepository repository,
    IOptions<WatcherOptions> options,
    ILogger<RegulatoryWatcher> logger,
    TimeProvider? clock = null)
{
    private readonly WatcherOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Fechas que repasa una pasada normal: hoy y los días de margen, de la más reciente a la más antigua.</summary>
    public IReadOnlyList<DateOnly> DefaultDates()
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        return Enumerable.Range(0, Math.Max(0, _options.LookbackDays) + 1).Select(d => today.AddDays(-d)).ToList();
    }

    /// <param name="simulate">
    /// Criba pero no descarga ni guarda nada: dice qué haría. Sigue gastando las llamadas baratas del cribado.
    /// </param>
    public async Task<WatchReport> RunAsync(IReadOnlyList<DateOnly> dates, bool simulate, CancellationToken ct)
    {
        // Lo gastado en la pasada entera: el cribado barato y, anidado, cada caso completo.
        using var pass = UsageScope.Begin();
        using var passSpan = Telemetry.Source.StartActivity("vigilante.pasada");
        passSpan?.SetTag("centinela.watcher.simulated", simulate);

        var all = await repository.ListSummariesAsync(null, ct);
        var known = all.ToLookup(s => s.SourceId, StringComparer.Ordinal);
        var todayUtc = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var expensiveToday = all
            .Count(s => s.Expensive && DateOnly.FromDateTime(s.SavedAt.UtcDateTime) == todayUtc);

        var items = new List<WatchItem>();
        var entriesSeen = 0;
        var screened = 0;
        var openedThisRun = 0;

        foreach (var date in dates)
        {
            IReadOnlyList<SourceEntry> entries;
            try { entries = await source.GetEntriesAsync(date, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "No se pudo leer el sumario del {Date}", date);
                items.Add(new($"sumario-{date:yyyy-MM-dd}", $"Sumario del {date:yyyy-MM-dd}", WatchOutcome.Error, e.Message));
                continue;
            }

            entriesSeen += entries.Count;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                items.Add(await HandleAsync(entry));
            }
        }

        foreach (var item in items)
        {
            Telemetry.Watched.Add(1, new KeyValuePair<string, object?>("outcome", item.Outcome.ToString()));
        }

        passSpan?.SetTag("centinela.watcher.entries", entriesSeen);
        return new WatchReport(dates, entriesSeen, items, simulate, pass.Snapshot());

        async Task<WatchItem> HandleAsync(SourceEntry entry)
        {
            WatchItem Item(WatchOutcome o, string detail) => new(entry.SourceId, entry.Title, o, detail);

            // 1. Lo que ya tiene caso no se repite; un caso fallido se reintenta unas pocas veces.
            var previous = known[entry.SourceId].ToList();
            if (previous.Any(c => c.Status != CaseStatus.Failed)) return Item(WatchOutcome.AlreadyKnown, "ya tiene un caso");
            if (previous.Count >= _options.MaxAttempts) return Item(WatchOutcome.RetriesExhausted, $"{previous.Count} intentos fallidos: lo debe mirar una persona");

            if (screened >= _options.MaxScreeningsPerRun) return Item(WatchOutcome.NotScreened, "tope de cribados por pasada");

            try
            {
                // 2. Guardián y cribado sobre lo poco que se sabe antes de descargar nada.
                var light = new RegulatoryChange(entry.SourceId, entry.SourceName, entry.Title, entry.Url, entry.Date,
                    $"Departamento: {entry.Department}\nSección: {entry.Section}");
                screened++;

                GuardResult verdict;
                bool relevant;
                using (UsageScope.Stage("Pre-cribado (título)"))
                {
                    verdict = await guard.InspectAsync(light, ct);
                    relevant = verdict.IsSafe && await screening.IsRelevantAsync(light, ct);
                }

                if (!verdict.IsSafe)
                {
                    if (!simulate)
                    {
                        var blocked = new ComplianceCase(light);
                        blocked.Fail($"Bloqueado por el guardián: {verdict.Reason}");
                        await repository.SaveAsync(blocked, ct);
                    }

                    return Item(WatchOutcome.BlockedByGuard, verdict.Reason ?? "");
                }

                if (!relevant)
                {
                    if (!simulate)
                    {
                        // Se guarda el descarte: es lo que impide volver a cribar mañana la misma publicación.
                        var discarded = new ComplianceCase(light);
                        discarded.MarkScreened(false);
                        await repository.SaveAsync(discarded, ct);
                    }

                    return Item(WatchOutcome.Irrelevant, "sin impacto probable");
                }

                // 3. Relevante: el flujo completo cuesta de verdad, así que manda el presupuesto.
                if (openedThisRun >= _options.MaxNewCasesPerRun) return Item(WatchOutcome.NoBudget, "tope de casos por pasada");
                if (expensiveToday + openedThisRun >= _options.MaxNewCasesPerDay) return Item(WatchOutcome.NoBudget, "tope de casos por día");

                openedThisRun++;
                if (simulate) return Item(WatchOutcome.WouldProcess, "se abriría un caso completo");

                var full = await source.GetFullChangeAsync(entry, ct);
                var result = await workflow.RunAsync(full, ct);
                return Item(WatchOutcome.Processed, $"{result.Status} · caso {result.Id}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Una publicación que falla no debe parar a las demás.
                logger.LogError(e, "Fallo procesando {SourceId}", entry.SourceId);
                return Item(WatchOutcome.Error, e.Message);
            }
        }
    }
}
