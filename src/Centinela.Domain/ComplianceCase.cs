namespace Centinela.Domain;

/// <summary>
/// Estados por los que pasa un caso de cumplimiento, de principio a fin.
/// Cada transición válida está definida en <see cref="ComplianceCase"/>.
/// </summary>
public enum CaseStatus
{
    Detected,
    Screened,
    Discarded,
    Analyzed,
    ImpactAssessed,
    UnderAudit,
    RevisionRequested,
    AwaitingHumanApproval,
    Approved,
    Rejected,
    Failed
}

/// <summary>Gravedad del impacto de un cambio normativo sobre la empresa.</summary>
public enum ImpactSeverity { None, Low, Medium, High, Critical }

/// <summary>Cambio normativo detectado en una fuente oficial (BOE, AEAT, AEPD...).</summary>
/// <param name="Sections">
/// Texto ya troceado en fragmentos citables. Es opcional: en el cribado basta con el título,
/// y solo el analista necesita el texto completo.
/// </param>
public sealed record RegulatoryChange(
    string SourceId,
    string Source,
    string Title,
    Uri Url,
    DateOnly PublishedOn,
    string RawText,
    IReadOnlyList<TextChunk>? Sections = null);

/// <summary>Una afirmación atómica del análisis, con los fragmentos que la respaldan.</summary>
public sealed record Claim(string Text, IReadOnlyList<string> Citations);

/// <summary>Cuánto respalda el texto citado a una afirmación.</summary>
public enum Support { Supported, Partial, Unsupported }

/// <summary>Veredicto del verificador sobre una afirmación concreta.</summary>
public sealed record ClaimVerdict(Claim Claim, Support Support, string Reason);

/// <summary>Resultado del análisis de un cambio: qué cambia exactamente respecto a la versión anterior.</summary>
/// <param name="AffectedArticles">Etiquetas de los fragmentos citados; las calcula el código, no el modelo.</param>
/// <param name="Claims">Afirmaciones atómicas, cada una con al menos una cita válida.</param>
/// <param name="Verdicts">Resultado de verificar si las citas respaldan las afirmaciones; vacío si no se verificó.</param>
public sealed record ChangeAnalysis(
    string Summary,
    IReadOnlyList<string> AffectedArticles,
    IReadOnlyList<string> Citations,
    IReadOnlyList<Claim>? Claims = null,
    IReadOnlyList<ClaimVerdict>? Verdicts = null)
{
    /// <summary>Afirmaciones que el verificador considera no respaldadas por lo que citan.</summary>
    public IEnumerable<ClaimVerdict> Unsupported =>
        (Verdicts ?? []).Where(v => v.Support == Support.Unsupported);
}

/// <summary>Un pasaje de un documento interno de la empresa afectado por el cambio.</summary>
/// <param name="PassageId">Identificador del pasaje (fragmento) del documento interno.</param>
/// <param name="PassageLabel">Sección del documento, p. ej. «2. Emisión de facturas».</param>
/// <param name="PassageQuote">Cita LITERAL del pasaje que muestra el problema; la verifica el código.</param>
/// <param name="NormCitations">Fragmentos de la norma que respaldan la obligación en juego.</param>
/// <param name="Obligations">Las obligaciones (afirmaciones del análisis) que el pasaje no cumple.</param>
/// <param name="SuggestedAction">Qué habría que cambiar en el documento.</param>
/// <param name="PassageText">Texto íntegro del pasaje afectado; es lo que el redactor reescribe.</param>
/// <param name="PassageEffect">«incumple», «falta_requisito» o «desfasado»: cómo afecta la norma a lo que dice la cita.</param>
/// <param name="Requirements">
/// Para cada obligación de <see cref="Obligations"/> (misma posición), el fragmento literal que de verdad se exige a
/// ESTE pasaje. Muchas obligaciones juntan varios deberes en una frase y a un pasaje solo le aplica uno.
/// </param>
public sealed record ImpactFinding(
    string DocumentId,
    string DocumentTitle,
    ImpactSeverity Severity,
    string Rationale,
    string? PassageId = null,
    string? PassageLabel = null,
    string? PassageQuote = null,
    IReadOnlyList<string>? NormCitations = null,
    IReadOnlyList<string>? Obligations = null,
    string? SuggestedAction = null,
    string? PassageText = null,
    string? PassageEffect = null,
    IReadOnlyList<string>? Requirements = null);

/// <summary>Dónde cumple el borrador una obligación: una cita LITERAL de su propio texto, que el código comprueba.</summary>
public sealed record ObligationCoverage(string Obligation, string HowAddressed, string Quote);

/// <summary>Acción correctora propuesta por el agente redactor: el pasaje reescrito, con su evidencia.</summary>
/// <param name="ProposedText">El pasaje completo reescrito (no un parche): lo que sustituiría al original.</param>
/// <param name="Deadline">Solo si la norma lo fija; el redactor no inventa fechas, así que normalmente es <c>null</c>.</param>
/// <param name="Coverage">Para cada obligación, dónde la cumple el texto propuesto.</param>
/// <param name="PendingData">Datos que solo la empresa conoce y que el borrador deja como «[COMPLETAR: …]».</param>
public sealed record CorrectiveAction(
    string DocumentId,
    string ProposedText,
    string Justification,
    DateOnly? Deadline,
    string? PassageId = null,
    string? PassageLabel = null,
    string? OriginalText = null,
    IReadOnlyList<string>? NormCitations = null,
    IReadOnlyList<ObligationCoverage>? Coverage = null,
    IReadOnlyList<string>? PendingData = null);

/// <summary>Veredicto del agente auditor sobre el trabajo del redactor.</summary>
public sealed record AuditVerdict(
    bool Passed,
    IReadOnlyList<string> Issues,
    int Revision);

/// <summary>
/// Agregado raíz. Concentra el estado de un cambio normativo mientras lo procesa la
/// cadena de agentes y protege que solo se produzcan transiciones válidas.
/// </summary>
public sealed class ComplianceCase
{
    /// <summary>Máximo de rondas redactor-auditor antes de escalar a una persona.</summary>
    public const int MaxRevisions = 3;

    private readonly List<ImpactFinding> _findings = [];
    private readonly List<CorrectiveAction> _actions = [];
    private readonly List<string> _log = [];
    private readonly List<ModelUsage> _usage = [];

    public Guid Id { get; }
    public RegulatoryChange Change { get; }
    public CaseStatus Status { get; private set; } = CaseStatus.Detected;
    public ChangeAnalysis? Analysis { get; private set; }
    public AuditVerdict? LastVerdict { get; private set; }
    public int Revision { get; private set; }
    public string? ReviewerComment { get; private set; }

    public IReadOnlyList<ImpactFinding> Findings => _findings;
    public IReadOnlyList<CorrectiveAction> Actions => _actions;
    public IReadOnlyList<string> Log => _log;

    /// <summary>Tokens que ha gastado el caso, por etapa y modelo. Se sobrescribe con el total de la última ejecución.</summary>
    public IReadOnlyList<ModelUsage> Usage => _usage;

    public void SetUsage(IEnumerable<ModelUsage> usage)
    {
        _usage.Clear();
        _usage.AddRange(usage);
    }

    public ComplianceCase(RegulatoryChange change)
    {
        Id = Guid.NewGuid();
        Change = change;
        Record("Caso creado");
    }

    // Solo para rehidratar un caso ya guardado: no registra nada en el historial ni valida transiciones.
    private ComplianceCase(CaseSnapshot s)
    {
        Id = s.Id;
        Change = s.Change;
        Status = s.Status;
        Analysis = s.Analysis;
        LastVerdict = s.LastVerdict;
        Revision = s.Revision;
        ReviewerComment = s.ReviewerComment;
        _findings.AddRange(s.Findings);
        _actions.AddRange(s.Actions);
        _log.AddRange(s.Log);
        _usage.AddRange(s.Usage ?? []);
    }

    /// <summary>Estado completo del caso, listo para guardar. Es una copia: no da acceso para saltarse las transiciones.</summary>
    public CaseSnapshot ToSnapshot() => new(
        Id, Change, Status, Analysis, LastVerdict, Revision, ReviewerComment,
        [.. _findings], [.. _actions], [.. _log], [.. _usage]);

    /// <summary>Reconstruye un caso desde un estado guardado. Las transiciones siguientes se validan igual que siempre.</summary>
    public static ComplianceCase Restore(CaseSnapshot snapshot) => new(snapshot);

    public void MarkScreened(bool isRelevant)
    {
        Require(CaseStatus.Detected);
        Status = isRelevant ? CaseStatus.Screened : CaseStatus.Discarded;
        Record(isRelevant ? "Pasa el cribado" : "Descartado por irrelevante");
    }

    public void SetAnalysis(ChangeAnalysis analysis)
    {
        Require(CaseStatus.Screened);
        Analysis = analysis;
        Status = CaseStatus.Analyzed;
        Record("Análisis normativo completado");
    }

    public void SetImpact(IEnumerable<ImpactFinding> findings)
    {
        Require(CaseStatus.Analyzed);
        _findings.AddRange(findings);
        // Si nada de la empresa se ve afectado no hay nada que redactar.
        Status = _findings.Any(f => f.Severity > ImpactSeverity.None)
            ? CaseStatus.ImpactAssessed
            : CaseStatus.Discarded;
        Record($"Impacto evaluado: {_findings.Count} documentos revisados");
    }

    public void SetDraft(IEnumerable<CorrectiveAction> actions)
    {
        Require(CaseStatus.ImpactAssessed, CaseStatus.RevisionRequested);
        _actions.Clear();
        _actions.AddRange(actions);
        Revision++;
        Status = CaseStatus.UnderAudit;
        Record($"Borrador v{Revision} redactado");
    }

    public void ApplyAudit(AuditVerdict verdict)
    {
        Require(CaseStatus.UnderAudit);
        LastVerdict = verdict;

        if (verdict.Passed)
        {
            Status = CaseStatus.AwaitingHumanApproval;
            Record("Auditoría superada, pendiente de aprobación humana");
        }
        else if (Revision >= MaxRevisions)
        {
            // Se agotan las rondas: una persona decide en lugar de seguir iterando.
            Status = CaseStatus.AwaitingHumanApproval;
            Record("Auditoría no superada tras el máximo de rondas; se escala a una persona");
        }
        else
        {
            Status = CaseStatus.RevisionRequested;
            Record($"Auditoría pide cambios ({verdict.Issues.Count} observaciones)");
        }
    }

    /// <summary>Riesgos que una persona debe reconocer de forma explícita antes de aprobar. Vacío si no hay ninguno.</summary>
    public IReadOnlyList<string> RisksToAcknowledge()
    {
        var risks = new List<string>();
        if (LastVerdict is { Passed: false })
        {
            risks.Add($"La auditoría NO se superó (escalado tras {Revision} ronda(s)): quedan {LastVerdict.Issues.Count} incidencia(s) sin resolver.");
        }

        var pending = _actions.Sum(a => a.PendingData?.Count ?? 0);
        if (pending > 0)
        {
            risks.Add($"Hay {pending} dato(s) «[COMPLETAR: …]» que la empresa debe aportar; el texto no es aplicable tal cual.");
        }

        return risks;
    }

    /// <param name="acknowledgeRisks">
    /// Hace falta <c>true</c> si el caso fue escalado sin superar la auditoría o tiene datos por completar. Nunca se asume.
    /// </param>
    public void Approve(string reviewer, bool acknowledgeRisks = false)
    {
        Require(CaseStatus.AwaitingHumanApproval);
        if (string.IsNullOrWhiteSpace(reviewer)) throw new ArgumentException("Hace falta el nombre de quien aprueba.", nameof(reviewer));

        var risks = RisksToAcknowledge();
        if (risks.Count > 0 && !acknowledgeRisks)
        {
            throw new RiskNotAcknowledgedException(risks);
        }

        Status = CaseStatus.Approved;
        Record(risks.Count > 0
            ? $"Aprobado por {reviewer} reconociendo expresamente: {string.Join(" ", risks)}"
            : $"Aprobado por {reviewer}");
    }

    public void Reject(string reviewer, string comment)
    {
        Require(CaseStatus.AwaitingHumanApproval);
        ReviewerComment = comment;
        Status = CaseStatus.Rejected;
        Record($"Rechazado por {reviewer}: {comment}");
    }

    /// <summary>Marca el caso como fallido. Un caso ya decidido por una persona no puede pasar a fallido.</summary>
    public void Fail(string reason)
    {
        if (Status is CaseStatus.Approved or CaseStatus.Rejected)
        {
            throw new InvalidOperationException($"Un caso {Status} es definitivo y no puede marcarse como fallido.");
        }

        Status = CaseStatus.Failed;
        Record($"Fallo: {reason}");
    }

    private void Require(params CaseStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException(
                $"Transición inválida desde {Status}; se esperaba {string.Join(" o ", allowed)}.");
        }
    }

    private void Record(string message) =>
        _log.Add($"{DateTimeOffset.UtcNow:O} | {message}");
}

/// <summary>Estado serializable de un <see cref="ComplianceCase"/>; es lo que se persiste.</summary>
public sealed record CaseSnapshot(
    Guid Id,
    RegulatoryChange Change,
    CaseStatus Status,
    ChangeAnalysis? Analysis,
    AuditVerdict? LastVerdict,
    int Revision,
    string? ReviewerComment,
    IReadOnlyList<ImpactFinding> Findings,
    IReadOnlyList<CorrectiveAction> Actions,
    IReadOnlyList<string> Log,
    IReadOnlyList<ModelUsage>? Usage = null);

/// <summary>Se intentó aprobar un caso con riesgos conocidos sin que la persona los reconociera.</summary>
public sealed class RiskNotAcknowledgedException(IReadOnlyList<string> risks)
    : InvalidOperationException("Para aprobar hay que reconocer: " + string.Join(" ", risks))
{
    public IReadOnlyList<string> Risks { get; } = risks;
}

/// <summary>Lo que gastó una etapa del flujo en un modelo: llamadas y tokens. Los tokens son lo que factura Azure.</summary>
public sealed record ModelUsage(string Stage, string Model, int Calls, long InputTokens, long OutputTokens);
