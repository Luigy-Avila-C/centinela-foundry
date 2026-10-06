using System.ClientModel;

namespace Centinela.Infrastructure.Foundry;

/// <summary>
/// Reintenta una llamada cuando el servicio responde "demasiadas peticiones" (HTTP 429). Los
/// despliegues tienen un tope de tokens por minuto; superarlo es normal en cargas en paralelo y
/// no debe tumbar el proceso, solo esperar.
/// </summary>
public static class RateLimitRetry
{
    public const int DefaultMaxAttempts = 12;

    /// <param name="retryAfter">
    /// Devuelve cuánto esperar si la excepción es un límite de velocidad, o <c>null</c> si es otro error
    /// (que se propaga sin reintentar). <see cref="TimeSpan.Zero"/> significa "espera la que toque".
    /// </param>
    /// <param name="delay">Espera; se inyecta para poder probar sin esperar de verdad.</param>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        Func<Exception, TimeSpan?> retryAfter,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (retryAfter(ex) is { } suggested && attempt < maxAttempts)
            {
                await delay(Backoff(attempt, suggested), ct);
            }
        }
    }

    /// <summary>
    /// Respeta la indicación del servidor si la hay; si no, espera 2, 4, 8… segundos con un tope.
    /// Se añade un margen de unos segundos porque el contador de tokens es por minuto.
    /// </summary>
    public static TimeSpan Backoff(int attempt, TimeSpan suggested)
    {
        var exponential = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
        return suggested > TimeSpan.Zero ? suggested + TimeSpan.FromSeconds(1) : exponential;
    }

    /// <summary>Detecta un 429 del SDK y lee su cabecera <c>Retry-After</c> si viene.</summary>
    public static TimeSpan? FromSdk(Exception ex)
    {
        if (ex is not ClientResultException { Status: 429 } e) return null;

        var header = e.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true ? value : null;
        return int.TryParse(header, out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    public static Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        ExecuteAsync(action, FromSdk, Task.Delay, ct);
}
