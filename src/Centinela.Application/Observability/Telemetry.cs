using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Centinela.Application;

/// <summary>
/// Fuente única de trazas y métricas de Centinela. Solo usa tipos del sistema (<c>System.Diagnostics</c>): la aplicación no
/// depende de OpenTelemetry. Sin nadie escuchando cuestan prácticamente nada; los exportadores se enganchan en la
/// infraestructura (<c>AddCentinelaTelemetry</c>) y solo si se activan.
/// <para>
/// <b>Privacidad:</b> las trazas llevan identificadores, nombres de etapa, modelos, estados y recuentos de tokens. NUNCA
/// el texto de los prompts, de las normas ni de los documentos de la empresa.
/// </para>
/// </summary>
public static class Telemetry
{
    public const string Name = "Centinela";

    public static readonly ActivitySource Source = new(Name, "1.0");
    public static readonly Meter Meter = new(Name, "1.0");

    /// <summary>Tokens gastados en modelos, con las etiquetas modelo, etapa y dirección (entrada o salida).</summary>
    public static readonly Counter<long> Tokens =
        Meter.CreateCounter<long>("centinela.tokens", "token", "Tokens gastados en modelos de lenguaje y embeddings");

    public static readonly Counter<long> ModelCalls =
        Meter.CreateCounter<long>("centinela.model.calls", "llamada", "Llamadas a modelos");

    /// <summary>Casos que terminan una ejecución del flujo, por estado final.</summary>
    public static readonly Counter<long> Cases =
        Meter.CreateCounter<long>("centinela.cases", "caso", "Casos procesados por el orquestador, por estado final");

    /// <summary>Publicaciones vistas por el vigilante, por resultado.</summary>
    public static readonly Counter<long> Watched =
        Meter.CreateCounter<long>("centinela.watcher.entries", "publicación", "Publicaciones tratadas por el vigilante, por resultado");

    /// <summary>Convención de OpenTelemetry para IA generativa: <c>gen_ai.*</c>.</summary>
    public static class Tag
    {
        public const string Model = "gen_ai.request.model";
        public const string InputTokens = "gen_ai.usage.input_tokens";
        public const string OutputTokens = "gen_ai.usage.output_tokens";
        public const string Operation = "gen_ai.operation.name";
        public const string Stage = "centinela.stage";
        public const string CaseId = "centinela.case.id";
        public const string SourceId = "centinela.source.id";
        public const string Status = "centinela.case.status";
    }
}
