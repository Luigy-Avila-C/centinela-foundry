namespace Centinela.Application;

/// <summary>
/// Puerta única hacia un modelo de lenguaje. Los agentes dependen de esta interfaz y no del SDK
/// de Foundry, de modo que su lógica (prompts, parseo de respuestas) se prueba sin red.
/// </summary>
public interface ILanguageModel
{
    /// <param name="deployment">Nombre del despliegue en Foundry (p. ej. "gpt-4.1-mini").</param>
    /// <param name="instructions">Instrucciones fijas del agente (el "system prompt").</param>
    /// <param name="input">Entrada variable; puede contener texto no confiable.</param>
    Task<string> CompleteAsync(
        string deployment, string instructions, string input, CancellationToken ct);
}

/// <summary>
/// La plataforma rechazó la entrada con su filtro de contenido (por ejemplo, un intento de jailbreak). No es un fallo
/// técnico: es una señal. El guardián de seguridad la trata como un hallazgo; cualquier otro agente la deja subir.
/// </summary>
public sealed class ContentFilteredException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Modelos que usa cada tipo de tarea. Se configuran, no se fijan en el código.</summary>
public sealed class ModelOptions
{
    public const string SectionName = "Models";

    /// <summary>Modelo barato y rápido: cribado y guardián de seguridad.</summary>
    public string Fast { get; set; } = "gpt-4.1-mini";

    /// <summary>Modelo potente: análisis, impacto, redacción y auditoría.</summary>
    public string Smart { get; set; } = "gpt-4.1";

    /// <summary>
    /// Modelo del verificador de citas. Debe ser distinto del que genera el análisis (<see cref="Smart"/>):
    /// un modelo tiende a aprobar su propio trabajo. Es de otra generación a propósito.
    /// </summary>
    public string Judge { get; set; } = "gpt-5.1";

    /// <summary>Modelo de embeddings para la búsqueda semántica (1536 dimensiones).</summary>
    public string Embedding { get; set; } = "text-embedding-3-small";
}
