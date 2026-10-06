// El SDK marca la API de Responses como experimental (OPENAI001). Se acepta de forma consciente
// y acotada a este archivo, que es el único que la usa; no se silencia en todo el proyecto.
#pragma warning disable OPENAI001

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Centinela.Application;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel.Primitives;
using OpenAI.Responses;

namespace Centinela.Infrastructure.Foundry;

/// <summary>Configuración de la conexión con el proyecto de Foundry.</summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>
    /// Endpoint del proyecto (https://&lt;recurso&gt;.services.ai.azure.com/api/projects/&lt;proyecto&gt;).
    /// No es un secreto: la autenticación es siempre por Microsoft Entra ID.
    /// </summary>
    public string ProjectEndpoint { get; set; } = "";

    /// <summary>
    /// Endpoint OpenAI de la cuenta (https://&lt;recurso&gt;.openai.azure.com/openai/v1/). Opcional: si
    /// se omite se deriva del endpoint del proyecto. Los embeddings no los sirve el endpoint del
    /// proyecto (devuelve 404), solo el de la cuenta.
    /// </summary>
    public string? OpenAiEndpoint { get; set; }
}

/// <summary>
/// Conexión única al proyecto de Foundry, compartida por el modelo de lenguaje y el de
/// embeddings. Es el único lugar que conoce el SDK; si cambia (y cambia a menudo), solo se toca aquí.
/// </summary>
public sealed class FoundryProject
{
    public ProjectOpenAIClient OpenAi { get; }

    /// <summary>Credencial compartida; se reutiliza para los embeddings.</summary>
    public DefaultAzureCredential Credential { get; } = new();

    /// <summary>Endpoint OpenAI de la cuenta, necesario para embeddings.</summary>
    public Uri OpenAiEndpoint { get; }

    public FoundryProject(IOptions<FoundryOptions> options)
    {
        var endpoint = options.Value.ProjectEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Falta Foundry:ProjectEndpoint. Defínelo en la configuración o con la variable " +
                "de entorno Foundry__ProjectEndpoint.");
        }

        // DefaultAzureCredential usa `az login` en local y la identidad administrada en Azure:
        // el mismo código sirve en ambos sitios y no hay ninguna clave que custodiar.
        var project = new AIProjectClient(new Uri(endpoint), Credential);
        OpenAi = project.ProjectOpenAIClient;

        OpenAiEndpoint = string.IsNullOrWhiteSpace(options.Value.OpenAiEndpoint)
            ? DeriveOpenAiEndpoint(new Uri(endpoint))
            : new Uri(options.Value.OpenAiEndpoint);
    }

    /// <summary>https://x.services.ai.azure.com/api/projects/p → https://x.openai.azure.com/openai/v1/</summary>
    public static Uri DeriveOpenAiEndpoint(Uri projectEndpoint)
    {
        const string foundryHost = ".services.ai.azure.com";
        if (!projectEndpoint.Host.EndsWith(foundryHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"No se puede derivar el endpoint OpenAI de '{projectEndpoint.Host}'. " +
                "Defínelo en Foundry:OpenAiEndpoint.");
        }

        var account = projectEndpoint.Host[..^foundryHost.Length];
        return new Uri($"https://{account}.openai.azure.com/openai/v1/");
    }
}

public sealed class FoundryLanguageModel(FoundryProject project) : ILanguageModel
{
    public async Task<string> CompleteAsync(
        string deployment, string instructions, string input, CancellationToken ct)
    {
        var client = project.OpenAi.GetProjectResponsesClientForModel(deployment);

        var request = new CreateResponseOptions(
            deployment, [ResponseItem.CreateUserMessageItem(input)])
        {
            Instructions = instructions,
            // Las tareas de cumplimiento deben ser lo más deterministas posible.
            Temperature = 0,
            // No se conserva en Foundry el texto procesado: el caso ya se guarda en nuestra base.
            StoredOutputEnabled = false,
        };

        // Un tramo por llamada. Solo se anotan modelo, etapa y tokens: nunca el texto del prompt ni de la respuesta.
        using var span = Telemetry.Source.StartActivity("gen_ai.responses", System.Diagnostics.ActivityKind.Client);
        span?.SetTag(Telemetry.Tag.Operation, "responses");
        span?.SetTag(Telemetry.Tag.Model, deployment);
        span?.SetTag(Telemetry.Tag.Stage, UsageScope.CurrentStageName);

        try
        {
            var result = await RateLimitRetry.ExecuteAsync(() => client.CreateResponseAsync(request, ct), ct);
            var tokensIn = result.Value.Usage?.InputTokenCount ?? 0;
            var tokensOut = result.Value.Usage?.OutputTokenCount ?? 0;
            UsageScope.Record(deployment, tokensIn, tokensOut);
            span?.SetTag(Telemetry.Tag.InputTokens, tokensIn);
            span?.SetTag(Telemetry.Tag.OutputTokens, tokensOut);
            return result.Value.GetOutputText();
        }
        catch (System.ClientModel.ClientResultException e) when (e.Status == 400 && e.Message.Contains("content_filter", StringComparison.Ordinal))
        {
            span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "content_filter");
            // El filtro de contenido de la plataforma rechazó la entrada (p. ej. un intento de jailbreak).
            throw new ContentFilteredException("La plataforma filtró la entrada por su política de contenido.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Solo el tipo de la excepción: su mensaje puede arrastrar texto del prompt.
            span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, e.GetType().Name);
            throw;
        }
    }
}

public sealed class FoundryEmbeddingModel(
    FoundryProject project, IOptions<ModelOptions> models) : IEmbeddingModel
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return [];

        // Los embeddings van por el endpoint OpenAI de la cuenta, no por el del proyecto.
        var client = new EmbeddingClient(
            models.Value.Embedding,
            new BearerTokenPolicy(project.Credential, "https://cognitiveservices.azure.com/.default"),
            new OpenAIClientOptions { Endpoint = project.OpenAiEndpoint });

        var result = await RateLimitRetry.ExecuteAsync(
            () => client.GenerateEmbeddingsAsync(texts, options: null, ct), ct);

        UsageScope.Record(models.Value.Embedding, result.Value.Usage?.InputTokenCount ?? 0, 0);

        // El servicio devuelve un vector por entrada; se conserva el orden de entrada.
        return result.Value.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()).ToList();
    }
}
