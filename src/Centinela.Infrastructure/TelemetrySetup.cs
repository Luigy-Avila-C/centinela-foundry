using Centinela.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Centinela.Infrastructure;

/// <summary>
/// Conecta las trazas y métricas de <see cref="Telemetry"/> a un exportador. Está <b>desactivado por defecto</b>: sin
/// <c>Telemetry:Enabled=true</c> no se exporta nada. Configuración:
/// <list type="bullet">
///   <item><c>Telemetry:Console</c>: escribe trazas y métricas en la consola (útil para ver qué ocurre).</item>
///   <item><c>Telemetry:OtlpEndpoint</c> o la variable estándar <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>: las envía por OTLP a
///   cualquier recolector (Jaeger, Grafana, el recolector de Azure Monitor…).</item>
/// </list>
/// No se incluye ningún exportador a un servicio de pago de Azure: sería un recurso más con su propio coste.
/// </summary>
public static class TelemetrySetup
{
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("Telemetry:Enabled");

    /// <summary>Para hosts que se ejecutan (API, Worker): el proveedor se integra en el ciclo de vida del host.</summary>
    public static IServiceCollection AddCentinelaTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        if (!IsEnabled(configuration)) return services;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => { t.AddSource(Telemetry.Name); AddExporters(t, configuration); })
            .WithMetrics(m => { m.AddMeter(Telemetry.Name); AddExporters(m, configuration); });
        return services;
    }

    /// <summary>
    /// Para el CLI, que no ejecuta el host sino que lo usa como contenedor: crea los proveedores a mano y hay que
    /// liberarlos al terminar para que se vacíen los datos pendientes. Devuelve <c>null</c> si está desactivado.
    /// </summary>
    public static IDisposable? StartStandalone(IConfiguration configuration, string serviceName)
    {
        if (!IsEnabled(configuration)) return null;

        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);
        var tracing = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource).AddSource(Telemetry.Name);
        AddExporters(tracing, configuration);
        var metrics = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource).AddMeter(Telemetry.Name);
        AddExporters(metrics, configuration);

        return new Pair(tracing.Build(), metrics.Build());
    }

    private static void AddExporters(TracerProviderBuilder builder, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Telemetry:Console")) builder.AddConsoleExporter();
        if (OtlpEndpoint(configuration) is { } endpoint) builder.AddOtlpExporter(o => o.Endpoint = endpoint);
    }

    private static void AddExporters(MeterProviderBuilder builder, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Telemetry:Console")) builder.AddConsoleExporter();
        if (OtlpEndpoint(configuration) is { } endpoint) builder.AddOtlpExporter(o => o.Endpoint = endpoint);
    }

    private static Uri? OtlpEndpoint(IConfiguration configuration)
    {
        var value = configuration["Telemetry:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private sealed class Pair(IDisposable tracer, IDisposable meter) : IDisposable
    {
        public void Dispose()
        {
            tracer.Dispose();
            meter.Dispose();
        }
    }
}
