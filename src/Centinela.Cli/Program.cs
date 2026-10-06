using Centinela.Cli;
using Centinela.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Centinela.Cli.CliUtil;

// Punto de entrada del CLI: solo despacha. Cada comando vive en su propia clase.
// Requiere `az login` y las variables de entorno Foundry__ProjectEndpoint y Search__Endpoint (y Cosmos__Endpoint para guardar casos).

const string Usage = """
    Uso: centinela <comando> [opciones]

    Normas del BOE
      cribar [yyyy-MM-dd] [--max N]          Criba las disposiciones de un día (¿afectan a una pyme?)
      indexar <BOE-A-...> [...]              Descarga e indexa normas en Azure AI Search
      analizar <BOE-A-...>                   Analiza una disposición con citas verificadas
      ver <BOE-A-...> [--desde N] [--hasta N] Muestra los fragmentos de una norma (no llama a ningún modelo)

    Flujo completo
      caso real [--norma ID] [--informe f.md]  Ejecuta todo el flujo con el analista real y muestra el consumo
      caso <resultado.json> [--ejecucion N]    Igual, reutilizando un análisis guardado
      vigilar [fechas] [--simular] [--max-casos N]  Una pasada del vigilante sobre el BOE
      empresa-indexar [carpeta]              Indexa los documentos internos de la empresa
      impacto <resultado.json>               Qué pasajes de la empresa afecta una norma ya analizada
      exportar-caso <id> <fichero.json>      Exporta un caso guardado (p. ej. para la demo)

    Seguridad
      inspeccionar <fichero>                 Pasa el guardián de seguridad por un texto

    Evaluación
      puerta [--umbrales f] [--salida f]     Pasa todas las evaluaciones contra los umbrales de regresión
      evaluar [--split dev|test|all]         Verificador de citas
      evaluar-guardian | evaluar-auditor | evaluar-impacto
      cobertura <BOE-A-...> | rejuzgar <resultado.json> | control-medidor | calibrar-medidor
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "ayuda")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => logging.AddFilter("System.Net.Http", LogLevel.Warning))
    .ConfigureServices((context, services) => services
        .AddCentinela(context.Configuration)
        .AddCentinelaEvaluation(context.Configuration))
    .Build();

// Ctrl+C cancela con elegancia: los comandos largos llaman a modelos de pago y deben poder pararse sin dejar nada a medias.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.Error.WriteLine(Environment.NewLine + "Cancelando…");
    cancellation.Cancel();
};
var ct = cancellation.Token;
var sp = host.Services;

// Trazas y métricas: desactivadas salvo que se pida (Telemetry__Enabled=true). Se liberan al terminar para vaciar lo pendiente.
using var telemetry = TelemetrySetup.StartStandalone(sp.GetRequiredService<IConfiguration>(), "centinela-cli");

return args[0] switch
{
    "cribar" => await NormCommands.ScreenAsync(sp, args[1..], ct),
    "indexar" when args.Length > 1 => await NormCommands.IndexAsync(sp, args[1..], ct),
    "analizar" when args.Length == 2 => await NormCommands.AnalyzeAsync(sp, args[1], ct),
    "ver" when args.Length >= 2 => await NormCommands.ViewAsync(sp, args[1..], ct),

    "caso" when args.Length >= 2 => await CaseCommand.RunAsync(sp, args[1..], ct),
    "vigilar" => await WatchCommand.RunAsync(sp, args[1..], ct),
    "empresa-indexar" => await ImpactCommands.IndexCompanyAsync(sp, args[1..], ct),
    "impacto" when args.Length >= 2 => await ImpactCommands.AssessAsync(sp, args[1..], ct),
    "exportar-caso" when args.Length >= 3 => await ExportCaseCommand.RunAsync(sp, args[1..], ct),

    "inspeccionar" when args.Length >= 2 => await GuardCommands.InspectAsync(sp, args[1..], ct),

    "puerta" => await GateCommand.RunAsync(sp, args[1..], ct),
    "evaluar" => await VerifierCommand.EvaluateAsync(sp, args[1..], ct),
    "evaluar-guardian" => await GuardCommands.EvaluateAsync(sp, args[1..], ct),
    "evaluar-auditor" => await DraftingCommands.EvaluateAuditorAsync(sp, args[1..], ct),
    "evaluar-impacto" when args.Length >= 2 => await ImpactCommands.EvaluateAsync(sp, args[1..], ct),
    "cobertura" when args.Length >= 2 => await CoverageCommands.CoverageAsync(sp, args[1..], ct),
    "rejuzgar" when args.Length >= 2 => await CoverageCommands.RejudgeAsync(sp, args[1..], ct),
    "control-medidor" when args.Length >= 2 => await MatcherControl.RunAsync(sp, args[1..], ct),
    "calibrar-medidor" => await MatcherControl.CalibrateAsync(sp, args[1..], ct),
    _ => Fail(Usage),
};
