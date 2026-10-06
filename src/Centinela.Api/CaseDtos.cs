using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Api;

/// <summary>Lo que ve el panel de un caso. Solo lectura: las decisiones van por los endpoints de aprobar y rechazar.</summary>
public sealed record CaseDetailDto(
    Guid Id,
    CaseStatus Status,
    string Title,
    string SourceId,
    Uri Url,
    DateOnly PublishedOn,
    int Revision,
    string? AnalysisSummary,
    int Obligations,
    int UnsupportedClaims,
    IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<ActionDto> Actions,
    bool AuditPassed,
    IReadOnlyList<string> AuditIssues,
    IReadOnlyList<string> RisksToAcknowledge,
    bool CanDecide,
    string? ReviewerComment,
    IReadOnlyList<string> Log,
    IReadOnlyList<UsageDto> Usage,
    decimal EstimatedUsd,
    IReadOnlyList<string> UnpricedModels)
{
    public static CaseDetailDto From(ComplianceCase c, PricingOptions pricing)
    {
        var chunks = (c.Change.Sections ?? []).ToDictionary(s => s.Id, StringComparer.Ordinal);

        return new CaseDetailDto(
            c.Id, c.Status, c.Change.Title, c.Change.SourceId, c.Change.Url, c.Change.PublishedOn, c.Revision,
            c.Analysis?.Summary,
            c.Analysis?.Claims?.Count ?? 0,
            c.Analysis?.Unsupported.Count() ?? 0,
            c.Findings.Where(f => f.Severity > ImpactSeverity.None).Select(f => new FindingDto(
                f.DocumentTitle, f.PassageLabel, f.Severity, f.Rationale, f.PassageQuote, f.PassageEffect, f.Obligations ?? [])).ToList(),
            c.Actions.Select(a => new ActionDto(
                a.DocumentId, a.PassageLabel, a.OriginalText, a.ProposedText, a.Justification,
                (a.Coverage ?? []).Select(x => new CoverageDto(x.Obligation, x.Quote)).ToList(),
                a.PendingData ?? [],
                (a.NormCitations ?? []).Select(id => chunks.TryGetValue(id, out var n)
                    ? new CitationDto(id, n.Label, n.Text)
                    : new CitationDto(id, null, null)).ToList())).ToList(),
            c.LastVerdict?.Passed ?? false,
            c.LastVerdict?.Issues ?? [],
            c.RisksToAcknowledge(),
            c.Status == CaseStatus.AwaitingHumanApproval,
            c.ReviewerComment,
            c.Log,
            c.Usage.Select(u => new UsageDto(u.Stage, u.Model, u.Calls, u.InputTokens, u.OutputTokens,
                CostEstimator.Estimate([u], pricing).Usd)).ToList(),
            CostEstimator.Estimate(c.Usage, pricing).Usd,
            CostEstimator.Estimate(c.Usage, pricing).UnpricedModels);
    }
}

public sealed record FindingDto(
    string Document, string? Passage, ImpactSeverity Severity, string Rationale, string? Quote, string? Effect, IReadOnlyList<string> Obligations);

public sealed record ActionDto(
    string DocumentId, string? Passage, string? Original, string Proposed, string Justification,
    IReadOnlyList<CoverageDto> Coverage, IReadOnlyList<string> PendingData, IReadOnlyList<CitationDto> Citations);

public sealed record UsageDto(string Stage, string Model, int Calls, long InputTokens, long OutputTokens, decimal EstimatedUsd);

public sealed record CoverageDto(string Obligation, string Quote);

public sealed record CitationDto(string Id, string? Label, string? Text);

public sealed record ApproveRequest(string? Reviewer, bool AcknowledgeRisks);

public sealed record RejectRequest(string? Reviewer, string? Comment);
