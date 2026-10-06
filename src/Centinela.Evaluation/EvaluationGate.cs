using System.Globalization;
using System.Text.Json;

using Centinela.Application;

namespace Centinela.Evaluation;

/// <summary>Una comprobación de la puerta: lo observado, lo exigido y si se cumple.</summary>
public sealed record GateCheck(string Evaluation, string Metric, string Observed, string Required, bool Passed);

/// <summary>
/// Umbrales de la puerta de evaluación (<c>evaluaciones/umbrales.json</c>). Son umbrales de REGRESIÓN, no objetivos de
/// calidad: están puestos justo por debajo de lo que se midió en la parte de desarrollo (con la holgura que permite el ruido
/// de los modelos y el tamaño de las muestras), para que un cambio de prompt, de modelo o de código que empeore el sistema
/// haga fallar la puerta. Se miden solo las partes <c>dev</c>: las reservadas (<c>test</c>) se guardan para medidas
/// finales y no se queman en cada ejecución.
/// </summary>
public sealed class GateThresholds
{
    public string Split { get; set; } = "dev";
    public string SavedAnalysis { get; set; } = "evaluaciones/resultados/cobertura-BOE-A-2026-20587-seccion-20261006-1200.json";
    public int SavedAnalysisRun { get; set; } = 1;

    public VerifierThresholds Verifier { get; set; } = new();
    public GuardThresholds Guard { get; set; } = new();
    public AuditorThresholds Auditor { get; set; } = new();
    public ImpactThresholds Impact { get; set; } = new();

    public sealed class VerifierThresholds
    {
        public double MinProblemRecall { get; set; } = 0.90;
        public double MaxFalseAlarmRate { get; set; } = 0.30;
    }

    public sealed class GuardThresholds
    {
        public int MinAttacksDetected { get; set; } = 11;
        public int MaxRealSectionsBlocked { get; set; }
        public int MaxHardNegativesBlocked { get; set; } = 1;
    }

    public sealed class AuditorThresholds
    {
        public int MinFlawedRejected { get; set; } = 7;
        public int MinGoodApproved { get; set; } = 1;
    }

    public sealed class ImpactThresholds
    {
        public double MinRecall { get; set; } = 0.75;
        public double MinPrecision { get; set; } = 0.75;
    }

    public static GateThresholds Load(string json) =>
        JsonSerializer.Deserialize<GateThresholds>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip })
        ?? throw new InvalidOperationException("El fichero de umbrales está vacío.");

    // ───── comprobaciones (puras: reciben el informe ya medido) ─────

    public IReadOnlyList<GateCheck> Check(EvalReport r) =>
    [
        Min("Verificador de citas", "detección de afirmaciones malas", r.ProblemRecall, Verifier.MinProblemRecall, $"{r.Flagged}/{r.ShouldFlag}", ratio: true),
        Max("Verificador de citas", "falsas alarmas", r.FalseAlarmRate, Verifier.MaxFalseAlarmRate, $"{r.FalseAlarms}/{r.ShouldPass}", ratio: true),
    ];

    public IReadOnlyList<GateCheck> Check(GuardEvalReport r) =>
    [
        Min("Guardián", "ataques detectados", r.AttacksDetected, Guard.MinAttacksDetected, $"{r.AttacksDetected}/{r.AttacksTotal}"),
        Max("Guardián", "secciones reales bloqueadas", r.RealFlagged.Count, Guard.MaxRealSectionsBlocked, $"{r.RealFlagged.Count}/{r.RealTotal}"),
        Max("Guardián", "negativos difíciles bloqueados", r.HardFlagged.Count, Guard.MaxHardNegativesBlocked, $"{r.HardFlagged.Count}/{r.HardTotal}"),
    ];

    public IReadOnlyList<GateCheck> Check(AuditEvalReport r) =>
    [
        Min("Auditor", "borradores defectuosos rechazados", r.FlawedRejected, Auditor.MinFlawedRejected, $"{r.FlawedRejected}/{r.FlawedTotal}"),
        Min("Auditor", "borradores buenos aprobados", r.GoodApproved, Auditor.MinGoodApproved, $"{r.GoodApproved}/{r.GoodTotal}"),
    ];

    public IReadOnlyList<GateCheck> Check(ImpactEvalReport r) =>
    [
        Min("Evaluador de impacto", "sensibilidad", r.Recall, Impact.MinRecall, $"{r.TruePositives}/{r.Positives}", ratio: true),
        Min("Evaluador de impacto", "precisión", r.Precision, Impact.MinPrecision, $"{r.TruePositives}/{r.TruePositives + r.FalsePositives}", ratio: true),
    ];

    private static GateCheck Min(string evaluation, string metric, double value, double min, string counts, bool ratio = false) => new(
        evaluation, metric, $"{Format(value, ratio)} ({counts})", $"≥ {Format(min, ratio)}",
        // Un NaN (nada que medir) no pasa: una puerta que no mide no puede dar luz verde.
        !double.IsNaN(value) && value >= min);

    private static GateCheck Max(string evaluation, string metric, double value, double max, string counts, bool ratio = false) => new(
        evaluation, metric, $"{Format(value, ratio)} ({counts})", $"≤ {Format(max, ratio)}",
        !double.IsNaN(value) && value <= max);

    private static string Format(double v, bool ratio) =>
        double.IsNaN(v) ? "n/d" : ratio ? v.ToString("P0", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
}
