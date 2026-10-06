using System.Text.Json;
using Centinela.Domain;

using Centinela.Application;

namespace Centinela.Evaluation;

/// <summary>Una muestra del conjunto de evaluación del guardián: un texto y si de verdad lleva un ataque.</summary>
public sealed record GuardSample(string Id, string Split, bool IsAttack, string Technique, string Text);

public sealed record GuardAttackSpec(string Id, string Split, string Technique, string Position, int Carrier, string Payload);

public sealed record GuardEvalReport(
    int AttacksTotal, int AttacksDetected, int DetectedByCodeOnly, int DetectedByModelOnly, int DetectedByBoth,
    int CodeDetected, int ModelDetected, int PlatformDetected,
    IReadOnlyList<string> Missed,
    int RealTotal, IReadOnlyList<string> RealFlagged,
    int HardTotal, IReadOnlyList<string> HardFlagged,
    int DiscardedModelFindings);

/// <summary>
/// Evaluación del guardián. Los ataques los escribió el autor del proyecto y se insertan en secciones REALES de la norma (el portador),
/// así que el texto alrededor es auténtico; lo que no es auténtico es la carga. Los negativos son las secciones reales
/// de la norma, más unos «negativos difíciles» escritos a propósito con vocabulario que podría confundir (inteligencia artificial,
/// «sistema», «ignorar» en sentido jurídico…). Los ataques y los negativos difíciles están en
/// <c>evaluaciones/guardian-inyecciones.json</c>.
/// </summary>
public static class GuardEvaluation
{
    public static (IReadOnlyList<GuardAttackSpec> Attacks, IReadOnlyList<(string Id, string Split, string Text)> Hard) LoadDataset(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var attacks = root.GetProperty("ataques").EnumerateArray().Select(a => new GuardAttackSpec(
            a.GetProperty("id").GetString()!,
            a.GetProperty("split").GetString()!,
            a.GetProperty("tecnica").GetString()!,
            a.GetProperty("posicion").GetString()!,
            a.GetProperty("portadora").GetInt32(),
            a.GetProperty("carga").GetString()!)).ToList();

        var hard = root.GetProperty("negativos_dificiles").EnumerateArray().Select(n => (
            n.GetProperty("id").GetString()!,
            n.GetProperty("split").GetString()!,
            n.GetProperty("texto").GetString()!)).ToList();

        return (attacks, hard);
    }

    /// <summary>
    /// Construye las muestras. Las secciones reales cortas hacen de portadoras y de negativos; las pares van a
    /// <c>dev</c> y las impares a <c>test</c>.
    /// </summary>
    public static IReadOnlyList<GuardSample> BuildSamples(
        IReadOnlyList<TextChunk> realSections,
        IReadOnlyList<GuardAttackSpec> attacks,
        IReadOnlyList<(string Id, string Split, string Text)> hard)
    {
        var samples = new List<GuardSample>();

        for (var i = 0; i < realSections.Count; i++)
        {
            samples.Add(new($"real-{i:D2}", i % 2 == 0 ? "dev" : "test", false, "seccion_real", realSections[i].Text));
        }

        foreach (var h in hard) samples.Add(new(h.Id, h.Split, false, "negativo_dificil", h.Text));

        foreach (var a in attacks)
        {
            var carrier = realSections[a.Carrier % realSections.Count].Text;
            samples.Add(new(a.Id, a.Split, true, a.Technique, Inject(carrier, a.Payload, a.Position)));
        }

        return samples;
    }

    /// <summary>Inserta la carga como un párrafo propio al inicio, en medio (tras la primera oración pasada la mitad) o al final.</summary>
    public static string Inject(string carrier, string payload, string position)
    {
        switch (position)
        {
            case "inicio": return $"{payload}\n{carrier}";
            case "final": return $"{carrier}\n{payload}";
            default:
                var cut = carrier.IndexOf(". ", carrier.Length / 2, StringComparison.Ordinal);
                return cut < 0
                    ? $"{carrier}\n{payload}"
                    : $"{carrier[..(cut + 1)]}\n{payload}\n{carrier[(cut + 2)..]}";
        }
    }

    public static GuardEvalReport Evaluate(IReadOnlyList<(GuardSample Sample, GuardScan Scan)> results)
    {
        var attacks = results.Where(r => r.Sample.IsAttack).ToList();
        bool Code((GuardSample, GuardScan Scan) r) => r.Scan.Findings.Any(f => f.Origin == "codigo");
        bool Model((GuardSample, GuardScan Scan) r) => r.Scan.Findings.Any(f => f.Origin == "modelo");

        var detected = attacks.Where(r => !r.Scan.IsSafe).ToList();
        var real = results.Where(r => r.Sample.Technique == "seccion_real").ToList();
        var hard = results.Where(r => r.Sample.Technique == "negativo_dificil").ToList();

        string Flag((GuardSample Sample, GuardScan Scan) r) =>
            $"{r.Sample.Id}: {string.Join(" | ", r.Scan.Findings.Take(2).Select(f => $"{f.Kind}/{f.Origin} «{Short(f.Quote)}»"))}";

        return new GuardEvalReport(
            attacks.Count, detected.Count,
            attacks.Count(r => Code(r) && !Model(r)),
            attacks.Count(r => Model(r) && !Code(r)),
            attacks.Count(r => Code(r) && Model(r)),
            attacks.Count(Code), attacks.Count(Model), attacks.Count(r => r.Scan.Findings.Any(f => f.Origin == "plataforma")),
            attacks.Where(r => r.Scan.IsSafe).Select(r => $"{r.Sample.Id} [{r.Sample.Technique}]").ToList(),
            real.Count, real.Where(r => !r.Scan.IsSafe).Select(Flag).ToList(),
            hard.Count, hard.Where(r => !r.Scan.IsSafe).Select(Flag).ToList(),
            results.Sum(r => r.Scan.DiscardedModelFindings));
    }

    private static string Short(string s) => s.Length <= 70 ? s : s[..70] + "…";
}
