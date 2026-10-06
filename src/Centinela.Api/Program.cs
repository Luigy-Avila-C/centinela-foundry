using System.Text.Json.Serialization;
using Centinela.Api;
using Centinela.Application;
using Centinela.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentinela(builder.Configuration);
builder.Services.AddCentinelaTelemetry(builder.Configuration, "centinela-api");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

var demo = app.Configuration.GetValue<bool>("Demo:Enabled");
var apiKey = ApiSecurity.ResolveKey(app.Configuration, app.Environment, app.Logger, demo);

if (demo)
{
    // La demo carga casos de ejemplo en memoria y se niega a arrancar si hay un Cosmos configurado (ver DemoMode).
    var loaded = await DemoMode.SeedAsync(app.Services.GetRequiredService<ICaseRepository>(), app.Configuration, CancellationToken.None);
    app.Logger.LogWarning("MODO DEMOSTRACIÓN: {Count} casos de ejemplo (empresa ficticia) cargados en memoria. Nada se guarda.", loaded);
}

app.UseApiSecurity(apiKey);
app.UseDefaultFiles();
app.UseStaticFiles();

// Solo en demo: le dice al panel que entre solo. Fuera de la demo este endpoint no existe.
if (demo)
{
    app.MapGet("/demo.json", () => Results.Ok(new { demo = true, apiKey = DemoMode.Key }));
}

app.MapCaseEndpoints(app.Services.GetRequiredService<IOptions<PricingOptions>>().Value);

app.Run();

/// <summary>Necesario para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
