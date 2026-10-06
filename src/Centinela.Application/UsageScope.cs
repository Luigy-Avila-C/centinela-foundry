using System.Collections.Concurrent;
using Centinela.Domain;

namespace Centinela.Application;

/// <summary>
/// Cuenta los tokens que gastan las llamadas a modelos mientras dura un ámbito (un caso, una pasada del vigilante…).
/// Usa estado ambiental (<see cref="AsyncLocal{T}"/>) a propósito: los agentes llaman al modelo en paralelo y por muchas
/// capas, y pasar un contador por todas ellas ensuciaría cada firma. Los ámbitos se anidan: lo gastado dentro de uno
/// interior también cuenta en los exteriores, así una pasada suma lo de todos sus casos.
/// </summary>
public sealed class UsageScope : IDisposable
{
    private static readonly AsyncLocal<UsageScope?> CurrentScope = new();
    private static readonly AsyncLocal<string?> CurrentStage = new();

    private readonly UsageScope? _parent;
    private readonly ConcurrentDictionary<(string Stage, string Model), (int Calls, long In, long Out)> _totals = new();

    private UsageScope(UsageScope? parent) => _parent = parent;

    public static UsageScope? Current => CurrentScope.Value;

    /// <summary>Etapa a la que se atribuye ahora mismo lo que se gaste, si hay alguna.</summary>
    public static string? CurrentStageName => CurrentStage.Value;

    public static UsageScope Begin()
    {
        var scope = new UsageScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Marca la etapa a la que se atribuye lo que se gaste hasta que se libere el resultado.</summary>
    public static IDisposable Stage(string name)
    {
        var previous = CurrentStage.Value;
        CurrentStage.Value = name;
        return new Restore(() => CurrentStage.Value = previous);
    }

    /// <summary>Apunta una llamada. Si no hay ámbito activo no hace nada (las pruebas y los comandos sueltos no miden).</summary>
    public static void Record(string model, long inputTokens, long outputTokens)
    {
        var stage = CurrentStage.Value ?? "(sin etapa)";

        // Métricas: se alimentan aquí, en el único punto por el que pasa cada llamada, igual que el contador por caso.
        Telemetry.ModelCalls.Add(1, new KeyValuePair<string, object?>(Telemetry.Tag.Model, model), new KeyValuePair<string, object?>(Telemetry.Tag.Stage, stage));
        Telemetry.Tokens.Add(inputTokens, new KeyValuePair<string, object?>(Telemetry.Tag.Model, model), new KeyValuePair<string, object?>(Telemetry.Tag.Stage, stage), new KeyValuePair<string, object?>("direction", "input"));
        Telemetry.Tokens.Add(outputTokens, new KeyValuePair<string, object?>(Telemetry.Tag.Model, model), new KeyValuePair<string, object?>(Telemetry.Tag.Stage, stage), new KeyValuePair<string, object?>("direction", "output"));
        for (var s = CurrentScope.Value; s is not null; s = s._parent)
        {
            s._totals.AddOrUpdate((stage, model),
                _ => (1, inputTokens, outputTokens),
                (_, t) => (t.Calls + 1, t.In + inputTokens, t.Out + outputTokens));
        }
    }

    public IReadOnlyList<ModelUsage> Snapshot() => _totals
        .Select(kv => new ModelUsage(kv.Key.Stage, kv.Key.Model, kv.Value.Calls, kv.Value.In, kv.Value.Out))
        .OrderBy(u => u.Stage, StringComparer.Ordinal).ThenBy(u => u.Model, StringComparer.Ordinal)
        .ToList();

    public void Dispose() => CurrentScope.Value = _parent;

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>Precio de un modelo en dólares por millón de tokens.</summary>
public sealed class ModelPrice
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>
    /// Precios de REFERENCIA (lista pública de OpenAI/Azure para despliegues estándar globales, en USD por millón de
    /// tokens). No son tu factura: dependen de la región, del tipo de despliegue y cambian. Compruébalos en la página de
    /// precios de Azure OpenAI y sobrescríbelos en la configuración (<c>Pricing:Models:&lt;modelo&gt;:InputPerMillion</c>).
    /// </summary>
    public Dictionary<string, ModelPrice> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4.1"] = new() { InputPerMillion = 2.00m, OutputPerMillion = 8.00m },
        ["gpt-4.1-mini"] = new() { InputPerMillion = 0.40m, OutputPerMillion = 1.60m },
        ["gpt-5.1"] = new() { InputPerMillion = 1.25m, OutputPerMillion = 10.00m },
        ["text-embedding-3-small"] = new() { InputPerMillion = 0.02m, OutputPerMillion = 0m },
    };
}

public sealed record CostEstimate(decimal Usd, IReadOnlyList<string> UnpricedModels);

/// <summary>Convierte tokens en dólares con la tabla de <see cref="PricingOptions"/>. Es una estimación, no una factura.</summary>
public static class CostEstimator
{
    public static CostEstimate Estimate(IEnumerable<ModelUsage> usage, PricingOptions pricing)
    {
        decimal total = 0;
        var unpriced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var u in usage)
        {
            if (!pricing.Models.TryGetValue(u.Model, out var price)) { unpriced.Add(u.Model); continue; }
            total += u.InputTokens / 1_000_000m * price.InputPerMillion + u.OutputTokens / 1_000_000m * price.OutputPerMillion;
        }

        return new CostEstimate(total, unpriced.ToList());
    }
}

/// <summary>Presenta el consumo como tabla de texto, con el coste estimado por fila y en total.</summary>
public static class UsageReport
{
    public static string Format(IReadOnlyList<ModelUsage> usage, PricingOptions pricing)
    {
        if (usage.Count == 0) return "(sin consumo registrado)";

        var lines = new List<string>
        {
            $"{"Etapa",-22}{"Modelo",-24}{"Llamadas",9}{"Entrada",12}{"Salida",11}{"USD est.",11}",
        };

        foreach (var u in usage)
        {
            var cost = CostEstimator.Estimate([u], pricing);
            lines.Add($"{u.Stage,-22}{u.Model,-24}{u.Calls,9:N0}{u.InputTokens,12:N0}{u.OutputTokens,11:N0}" +
                      $"{(cost.UnpricedModels.Count > 0 ? "sin precio" : cost.Usd.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)),11}");
        }

        var total = CostEstimator.Estimate(usage, pricing);
        lines.Add(new string('─', 89));
        lines.Add($"{"TOTAL",-46}{usage.Sum(u => u.Calls),9:N0}{usage.Sum(u => u.InputTokens),12:N0}{usage.Sum(u => u.OutputTokens),11:N0}" +
                  $"{total.Usd.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),11}");
        lines.Add("USD estimados con precios de referencia (configurables en Pricing:Models); NO es tu factura.");
        if (total.UnpricedModels.Count > 0) lines.Add($"Sin precio configurado (no suman): {string.Join(", ", total.UnpricedModels)}.");
        return string.Join('\n', lines);
    }
}
