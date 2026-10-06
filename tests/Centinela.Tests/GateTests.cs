using Centinela.Application;

namespace Centinela.Tests;

public class GateTests
{
    private static readonly GateThresholds T = new();

    private static EvalReport Verifier(int shouldFlag, int flagged, int shouldPass, int falseAlarms) =>
        new(shouldFlag + shouldPass, 0, shouldFlag, flagged, 0, 0, shouldPass, falseAlarms, new int[3, 3]);

    private static GuardEvalReport Guard(int attacks, int detected, int realFlagged, int hardFlagged) => new(
        attacks, detected, 0, 0, 0, 0, 0, 0, [], 8, Enumerable.Repeat("r", realFlagged).ToList(), 4, Enumerable.Repeat("h", hardFlagged).ToList(), 0);

    private static AuditEvalReport Auditor(int good, int approved, int flawed, int rejected) =>
        new(good, approved, flawed, rejected, 0, 0, 0, 0, 0, [], []);

    private static ImpactEvalReport Impact(int tp, int fp, int fn) =>
        new(tp, fp, fn, 0, 0, 0, tp + fn, 0, 0, 0, [], [], []);

    [Fact]
    public void The_measured_baseline_passes_every_check()
    {
        var checks = T.Check(Verifier(9, 9, 7, 1))
            .Concat(T.Check(Guard(12, 12, 0, 0)))
            .Concat(T.Check(Auditor(4, 1, 8, 7)))
            .Concat(T.Check(Impact(4, 0, 0)))
            .ToList();

        Assert.All(checks, c => Assert.True(c.Passed, $"{c.Evaluation}/{c.Metric}: {c.Observed} vs {c.Required}"));
        Assert.Equal(9, checks.Count);
    }

    [Theory]
    [InlineData(9, 8, 7, 1)]   // detección 89 % < 90 %
    [InlineData(9, 9, 7, 3)]   // falsas alarmas 43 % > 30 %
    public void A_regression_in_the_verifier_fails_the_gate(int shouldFlag, int flagged, int shouldPass, int alarms)
    {
        Assert.Contains(T.Check(Verifier(shouldFlag, flagged, shouldPass, alarms)), c => !c.Passed);
    }

    [Theory]
    [InlineData(10, 0, 0)]   // se escapan 2 ataques
    [InlineData(12, 1, 0)]   // bloquea una sección real de la norma
    [InlineData(12, 0, 2)]   // bloquea 2 negativos difíciles
    public void A_regression_in_the_guard_fails_the_gate(int detected, int real, int hard)
    {
        Assert.Contains(T.Check(Guard(12, detected, real, hard)), c => !c.Passed);
    }

    [Theory]
    [InlineData(6, 3)]   // deja pasar 2 defectuosos
    [InlineData(8, 0)]   // rechaza TODOS los buenos: el auditor ha colapsado
    public void A_regression_in_the_auditor_fails_the_gate(int rejected, int approved)
    {
        Assert.Contains(T.Check(Auditor(4, approved, 8, rejected)), c => !c.Passed);
    }

    [Theory]
    [InlineData(2, 0, 2)]   // sensibilidad 50 %
    [InlineData(2, 2, 0)]   // precisión 50 %
    public void A_regression_in_the_impact_agent_fails_the_gate(int tp, int fp, int fn)
    {
        Assert.Contains(T.Check(Impact(tp, fp, fn)), c => !c.Passed);
    }

    [Fact]
    public void Nothing_to_measure_cannot_pass()
    {
        // 0 afirmaciones que marcar: la tasa es NaN. Una puerta que no mide no puede dar luz verde.
        Assert.All(T.Check(Verifier(0, 0, 0, 0)), c => Assert.False(c.Passed));
        Assert.All(T.Check(Impact(0, 0, 0)), c => Assert.False(c.Passed));
    }

    [Fact]
    public void The_shipped_thresholds_file_loads_and_matches_the_defaults_in_code()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "evaluaciones", "umbrales.json"))) dir = Path.GetDirectoryName(dir);

        var loaded = GateThresholds.Load(File.ReadAllText(Path.Combine(dir!, "evaluaciones", "umbrales.json")));

        Assert.Equal("dev", loaded.Split);
        Assert.Equal(T.Verifier.MinProblemRecall, loaded.Verifier.MinProblemRecall);
        Assert.Equal(T.Guard.MinAttacksDetected, loaded.Guard.MinAttacksDetected);
        Assert.Equal(T.Auditor.MinFlawedRejected, loaded.Auditor.MinFlawedRejected);
        Assert.Equal(T.Impact.MinPrecision, loaded.Impact.MinPrecision);
        Assert.True(File.Exists(Path.Combine(dir!, loaded.SavedAnalysis)));
    }
}
