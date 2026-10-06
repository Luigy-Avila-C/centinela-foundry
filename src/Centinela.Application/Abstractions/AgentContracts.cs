using Centinela.Domain;

namespace Centinela.Application;

// Contratos de los agentes. La aplicación solo conoce estas interfaces; la
// implementación con Azure AI Foundry vive en Centinela.Infrastructure. Así se puede
// probar toda la orquestación con dobles de prueba, sin llamar a ningún modelo.

/// <summary>Guardián de seguridad: revisa el texto de entrada antes de que lo vea ningún otro agente.</summary>
public interface ISecurityGuardAgent
{
    /// <summary>Detecta intentos de prompt injection o contenido inseguro en una fuente externa.</summary>
    Task<GuardResult> InspectAsync(RegulatoryChange change, CancellationToken ct);
}

public sealed record GuardResult(bool IsSafe, string? Reason);

/// <summary>Clasificador: decide si el cambio es relevante para la empresa.</summary>
public interface IScreeningAgent
{
    Task<bool> IsRelevantAsync(RegulatoryChange change, CancellationToken ct);
}

/// <summary>Analista normativo: explica qué cambia respecto a la versión anterior, con citas.</summary>
public interface IRegulatoryAnalystAgent
{
    Task<ChangeAnalysis> AnalyzeAsync(RegulatoryChange change, CancellationToken ct);
}

/// <summary>Evaluador de impacto: cruza el cambio con los documentos internos de la empresa.</summary>
public interface IImpactAgent
{
    Task<IReadOnlyList<ImpactFinding>> AssessAsync(
        ChangeAnalysis analysis, CancellationToken ct);
}

/// <summary>Redactor: propone el texto corregido de cada documento afectado.</summary>
public interface IDrafterAgent
{
    /// <param name="previousIssues">Observaciones del auditor en la ronda anterior, si las hay.</param>
    Task<IReadOnlyList<CorrectiveAction>> DraftAsync(
        ComplianceCase complianceCase,
        IReadOnlyList<string> previousIssues,
        CancellationToken ct);
}

/// <summary>Auditor: revisa el borrador de forma independiente (patrón generador-crítico).</summary>
public interface IAuditorAgent
{
    Task<AuditVerdict> AuditAsync(ComplianceCase complianceCase, CancellationToken ct);
}
