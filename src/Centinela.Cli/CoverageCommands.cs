using Centinela.Application;
using Centinela.Domain;
using Centinela.Infrastructure.Boe;
using Microsoft.Extensions.DependencyInjection;
using static Centinela.Cli.CliUtil;

namespace Centinela.Cli;

/// <summary>Comandos de medición de la cobertura de la norma: <c>cobertura</c> y <c>rejuzgar</c>.</summary>
internal static class CoverageCommands
{
    // centinela rejuzgar <resultado.json> [--referencia ruta] [--jueces a,b]
    // Mide la cobertura de afirmaciones YA generadas y guardadas, con la referencia actual. No regenera nada.
    public static async Task<int> RejudgeAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var path = args[0];
        var referencePath = Path.Combine("evaluaciones", "cobertura-orden-hac-1028-2026.json");
        var judgeNames = new[] { "gpt-4.1", "gpt-5.1" };
        string? group = null;

        for (var i = 1; i < args.Length - 1; i += 2)
        {
            switch (args[i])
            {
                case "--referencia": referencePath = args[i + 1]; break;
                case "--jueces": judgeNames = args[i + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case "--grupo": group = args[i + 1]; break;
                default: return Fail($"Argumento no reconocido: {args[i]}");
            }
        }

        if (!File.Exists(path)) return Fail($"No existe: {path}");

        var reference = CoverageReference.Load(await File.ReadAllTextAsync(referencePath, ct));
        if (group is not null)
        {
            // Solo se vuelven a medir los hechos del grupo pedido; el resto ya está medido y no hay que pagarlo otra vez.
            reference = reference with { Facts = reference.Facts.Where(f => f.Group == group).ToList() };
            if (reference.Facts.Count == 0) return Fail($"No hay hechos en el grupo '{group}'.");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var claimSets = doc.RootElement.GetProperty("Runs").EnumerateArray()
            .Select(run => (IReadOnlyList<string>)run.GetProperty("Claims").EnumerateArray()
                .Select(c => c.GetProperty("Text").GetString()!).ToList())
            .ToList();

        var llm = services.GetRequiredService<ILanguageModel>();
        var judges = judgeNames.Select(n => new JudgeSpec(
            n, n, new CitationVerifierAgent(llm, Microsoft.Extensions.Options.Options.Create(new ModelOptions { Judge = n })))).ToList();

        Console.WriteLine($"Remidiendo {claimSets.Count} conjuntos ({string.Join(", ", claimSets.Select(s => s.Count))} afirmaciones) " +
                          $"contra {reference.Facts.Count} hechos con {judges.Count} jueces: ≈{claimSets.Count * judges.Count * reference.Facts.Count} llamadas.\n");

        var covered = await services.GetRequiredService<CoverageExperiment>()
            .RejudgeAsync(claimSets, reference, judges, msg => Console.WriteLine($"  … {msg}"), ct);

        Console.WriteLine("\nCOBERTURA POR JUEZ Y GRUPO");
        PrintFactCoverage(reference, judgeNames, claimSets.Count, (judge, run) => covered[run][judge]);
        return 0;
    }

    // centinela cobertura <BOE-A-...> [--repeticiones 3] [--jueces gpt-4.1,gpt-5.1]
    //                     [--referencia ruta] [--salida ruta]
    // Genera el análisis N veces y juzga LAS MISMAS afirmaciones con cada modelo: veredicto de cada
    // afirmación y cobertura de los hechos de referencia. Es caro (cientos de llamadas): mira el aviso.
    public static async Task<int> CoverageAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var id = args[0];
        var repetitions = 3;
        var judgeNames = new[] { "gpt-4.1", "gpt-5.1" };
        var referencePath = Path.Combine("evaluaciones", "cobertura-orden-hac-1028-2026.json");
        string? output = null;
        string? resumePath = null;
        var mode = AnalysisMode.Single;

        for (var i = 1; i < args.Length - 1; i += 2)
        {
            var value = args[i + 1];
            switch (args[i])
            {
                case "--repeticiones" when int.TryParse(value, out var n) && n > 0: repetitions = n; break;
                case "--jueces": judgeNames = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case "--referencia": referencePath = value; break;
                case "--salida": output = value; break;
                case "--reanudar": resumePath = value; break;
                case "--modo" when value is "unico" or "seccion":
                    mode = value == "seccion" ? AnalysisMode.PerSection : AnalysisMode.Single; break;
                default: return Fail($"Argumento no reconocido: {args[i]} {value}");
            }
        }

        if (judgeNames.Length != 2) return Fail("Se necesitan exactamente dos jueces: --jueces modelo1,modelo2");
        if (!File.Exists(referencePath)) return Fail($"No existe la referencia: {referencePath}");

        var reference = CoverageReference.Load(await File.ReadAllTextAsync(referencePath, ct));
        if (reference.DocumentId != id) return Fail($"La referencia es de {reference.DocumentId}, no de {id}.");

        var document = await services.GetRequiredService<BoeClient>().GetDocumentAsync(id, ct);
        var chunks = DocumentChunker.Chunk(document);

        // Si el troceado cambió, los hechos de referencia apuntarían a fragmentos que ya no son los mismos.
        var missing = reference.Facts.Select(f => f.ChunkId).Distinct().Where(c => chunks.All(x => x.Id != c)).ToList();
        if (missing.Count > 0) return Fail($"La referencia apunta a fragmentos que ya no existen: {string.Join(", ", missing)}");

        var llm = services.GetRequiredService<ILanguageModel>();
        var judges = judgeNames.Select(name => new JudgeSpec(
            name, name,
            new CitationVerifierAgent(llm, Microsoft.Extensions.Options.Options.Create(new ModelOptions { Judge = name })))).ToList();

        // Estimación honesta: el modo por artículo genera una llamada por artículo y produce unas 2-3 veces más
        // afirmaciones, y cada afirmación se verifica con cada juez.
        var perSection = mode == AnalysisMode.PerSection;
        var generationCalls = perSection ? RegulatoryAnalystAgent.OperativeSections(chunks, new AnalysisOptions()).Count : 1;
        var claimsEstimate = perSection ? 100 : 42;
        var calls = repetitions * (generationCalls + judges.Count * (claimsEstimate + reference.Facts.Count))
                    + judges.Count * reference.Calibration.Count;
        Console.WriteLine($"AVISO: ≈{calls} llamadas a modelos ({repetitions} repeticiones × {judges.Count} jueces, " +
                          $"≈{claimsEstimate} afirmaciones por ejecución). Se pagan con tu crédito; los modelos de razonamiento son los más caros.\n");

        var change = new RegulatoryChange(
            document.Id, "BOE", document.Title, document.Url, document.PublishedOn,
            string.Join("\n", document.Paragraphs.Select(p => p.Text)), chunks);

        // El resultado completo se guarda para poder auditar las cifras, y se vuelve a guardar tras cada
        // ejecución: si la siguiente falla, lo ya medido (y pagado) no se pierde.
        // Al reanudar se sigue escribiendo en el mismo fichero, para no dispersar un experimento en varios.
        output ??= resumePath ?? Path.Combine("evaluaciones", "resultados", $"cobertura-{id}-{(mode == AnalysisMode.PerSection ? "seccion" : "unico")}-{DateTime.Now:yyyyMMdd-HHmm}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        ExperimentResult? resume = null;
        if (resumePath is not null)
        {
            if (!File.Exists(resumePath)) return Fail($"No existe el resultado a reanudar: {resumePath}");
            resume = System.Text.Json.JsonSerializer.Deserialize<ExperimentResult>(await File.ReadAllTextAsync(resumePath, ct), jsonOptions);
            if (resume is null) return Fail("El resultado a reanudar está vacío o es ilegible.");
        }

        var savePath = output;
        void Save(ExperimentResult r) =>
            File.WriteAllText(savePath, System.Text.Json.JsonSerializer.Serialize(r, jsonOptions));

        // Un analista con el modo pedido: así se comparan los dos métodos con el mismo experimento.
        var analyst = new RegulatoryAnalystAgent(
            llm, services.GetRequiredService<IEmbeddingModel>(), services.GetRequiredService<IChunkIndex>(),
            judges[0].Verifier, services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ModelOptions>>(),
            Microsoft.Extensions.Options.Options.Create(new AnalysisOptions { Mode = mode }));

        Console.WriteLine($"Modo de análisis: {(mode == AnalysisMode.PerSection ? "POR ARTÍCULO" : "ÚNICO (una llamada)")}\n");

        var result = await new CoverageExperiment(analyst, services.GetRequiredService<ICoverageMatcher>()).RunAsync(
            change, reference, judges, repetitions, msg => Console.WriteLine($"  … {msg}"), ct, Save, resume);

        Save(result);
        PrintCoverage(result, reference, output);
        return 0;
    }

    // Cobertura de hechos por juez y por grupo ("base" / "ampliacion"), a partir de lo que cada juez dio por cubierto.
    private static void PrintFactCoverage(
        CoverageReference reference, IReadOnlyList<string> judges, int runs,
        Func<string, int, IReadOnlyCollection<string>> covered)
    {
        string Pct(double d) => double.IsNaN(d) ? "n/d" : $"{d:P0}";

        foreach (var group in reference.Facts.Select(f => f.Group).Distinct())
        {
            var facts = reference.Facts.Where(f => f.Group == group).ToList();
            Console.WriteLine($"  Grupo «{group}» ({facts.Count} hechos)");

            foreach (var judge in judges)
            {
                var counts = Enumerable.Range(0, runs).Select(r => facts.Count(f => covered(judge, r).Contains(f.Id))).ToList();
                var perRun = counts.Select(c => c / (double)facts.Count).ToList();
                var every = facts.Count(f => Enumerable.Range(0, runs).All(r => covered(judge, r).Contains(f.Id)));
                var some = facts.Count(f => Enumerable.Range(0, runs).Any(r => covered(judge, r).Contains(f.Id)));
                var never = facts.Where(f => Enumerable.Range(0, runs).All(r => !covered(judge, r).Contains(f.Id))).Select(f => f.Id).ToList();

                Console.WriteLine($"    {judge,-9} por ejecución {string.Join(", ", counts.Select(c => $"{c}/{facts.Count}"))}" +
                                  $" → media {Pct(perRun.Average())} (mín {Pct(perRun.Min())} · máx {Pct(perRun.Max())})");
                Console.WriteLine($"    {"",-9} en TODAS: {every}/{facts.Count} · en ALGUNA: {some}/{facts.Count} · nunca: {(never.Count == 0 ? "ninguno" : string.Join(", ", never))}");
            }
        }
    }

    private static void PrintCoverage(ExperimentResult result, CoverageReference reference, string savedTo)
    {
        string Pct(double d) => double.IsNaN(d) ? "n/d" : $"{d:P0}";
        var names = result.Judges;

        Console.WriteLine("\n══ 1. CALIBRACIÓN DEL MEDIDOR (¿sabe distinguir cubierto de no cubierto?) ══");
        foreach (var c in result.Calibration)
        {
            Console.WriteLine($"  {c.Judge,-10} {c.Correct}/{c.Total} correctos" +
                              (c.Failures.Count > 0 ? $"   fallos: {string.Join(", ", c.Failures)}" : ""));
        }

        Console.WriteLine("\n══ 2. EJECUCIONES ══");
        foreach (var run in result.Runs)
        {
            Console.WriteLine($"  #{run.Index}: {run.Claims.Count} afirmaciones · fragmentos citados {Pct(run.ChunkCoverage)}");
        }

        Console.WriteLine("\n══ 3. COBERTURA DE HECHOS DE REFERENCIA, POR JUEZ Y GRUPO ══");
        PrintFactCoverage(reference, names, result.Runs.Count, (judge, run) => result.Runs[run].ByJudge[judge].CoveredFacts);

        // Desacuerdo del medidor: mismo hecho, mismas afirmaciones, distinto juez.
        var a = names[0];
        var b = names[1];
        var factDecisions = result.Runs.SelectMany(r => reference.Facts.Select(f =>
            (A: r.ByJudge[a].CoveredFacts.Contains(f.Id), B: r.ByJudge[b].CoveredFacts.Contains(f.Id)))).ToList();
        var agree = factDecisions.Count(d => d.A == d.B);
        Console.WriteLine($"\n  Los dos jueces coinciden en {agree}/{factDecisions.Count} decisiones de cobertura ({Pct((double)agree / factDecisions.Count)}). " +
                          $"Solo {a} dice «cubierto»: {factDecisions.Count(d => d.A && !d.B)} · solo {b}: {factDecisions.Count(d => !d.A && d.B)}");

        Console.WriteLine("\n══ 4. ACUERDO ENTRE JUECES SOBRE LAS MISMAS AFIRMACIONES (veredicto de citas) ══");
        var va = result.Runs.SelectMany(r => r.ByJudge[a].Verdicts).ToList();
        var vb = result.Runs.SelectMany(r => r.ByJudge[b].Verdicts).ToList();
        var s = AgreementStats.Compute(va, vb);
        Console.WriteLine($"  {s.N} veredictos comparados · acuerdo exacto {Pct(s.Exact)} · acuerdo en marcar/no marcar {Pct(s.FlagAgreement)} · kappa {(double.IsNaN(s.Kappa) ? "n/d" : s.Kappa.ToString("F2"))}");
        Console.WriteLine($"  {"",-16}{b + ": resp.",14}{"parcial",10}{"no resp.",10}");
        foreach (var (label, row) in new[] { ("respaldada", Support.Supported), ("parcial", Support.Partial), ("no respaldada", Support.Unsupported) })
        {
            Console.WriteLine($"  {a + " " + label,-16}{s.Confusion[(int)row, 0],14}{s.Confusion[(int)row, 1],10}{s.Confusion[(int)row, 2],10}");
        }

        var disagreements = new List<string>();
        foreach (var run in result.Runs)
        {
            for (var i = 0; i < run.Claims.Count; i++)
            {
                var ja = run.ByJudge[a];
                var jb = run.ByJudge[b];
                if (ja.Verdicts[i] == jb.Verdicts[i]) continue;
                disagreements.Add($"  #{run.Index} {a}={ja.Verdicts[i]} · {b}={jb.Verdicts[i]}\n     {run.Claims[i].Text}\n" +
                                  $"     {(jb.Verdicts[i] != Support.Supported ? b + ": " + jb.Reasons[i] : a + ": " + ja.Reasons[i])}");
            }
        }

        Console.WriteLine($"\n  Desacuerdos: {disagreements.Count}" + (disagreements.Count > 0 ? " (se muestran hasta 8)" : ""));
        foreach (var d in disagreements.Take(8)) Console.WriteLine(d);

        Console.WriteLine($"\nResultado completo guardado en: {savedTo}");
    }
}
