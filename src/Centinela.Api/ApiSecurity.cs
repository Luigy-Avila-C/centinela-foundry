using System.Security.Cryptography;
using System.Text;

namespace Centinela.Api;

/// <summary>
/// Seguridad de la API. Sin identidad de usuario (Entra ID) la clave compartida es lo único que separa el panel de cualquiera que
/// llegue al puerto, por eso la API nunca arranca sin ella (salvo en desarrollo y en la demo, ambos solo locales).
/// </summary>
public static class ApiSecurity
{
    /// <summary>Obtiene la clave de la API o se niega a arrancar si no hay ninguna que sea segura de usar.</summary>
    public static string ResolveKey(IConfiguration configuration, IHostEnvironment environment, ILogger logger, bool demo)
    {
        var key = configuration["Api:Key"];
        if (!string.IsNullOrWhiteSpace(key)) return key;

        // La demo es solo local y en memoria: una clave fija evita pedirle nada a quien solo quiere verla.
        if (demo) return DemoMode.Key;

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Falta Api:Key. Defínela (variable de entorno Api__Key) antes de arrancar la API.");
        }

        key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        logger.LogWarning("Api:Key no está definida: se ha generado una clave temporal para esta ejecución: {Key}", key);
        return key;
    }

    /// <summary>
    /// Cabeceras de seguridad para todo y comprobación de la clave (en tiempo constante) para <c>/api</c>. El panel evita
    /// cualquier script o estilo en línea: lo que muestra viene de modelos y no se puede confiar en ello.
    /// </summary>
    public static IApplicationBuilder UseApiSecurity(this IApplicationBuilder app, string apiKey)
    {
        var expected = Encoding.UTF8.GetBytes(apiKey);

        return app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var given = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
                if (!CryptographicOperations.FixedTimeEquals(given, expected))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "Falta o no es válida la cabecera X-Api-Key." });
                    return;
                }
            }

            await next();
        });
    }
}
