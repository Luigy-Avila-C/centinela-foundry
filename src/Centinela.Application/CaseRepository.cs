using Centinela.Domain;

namespace Centinela.Application;

/// <summary>Persistencia de los casos (Cosmos DB en producción).</summary>
public interface ICaseRepository
{
    /// <exception cref="CaseConflictException">Otra persona o proceso modificó el caso desde que se leyó.</exception>
    Task SaveAsync(ComplianceCase complianceCase, CancellationToken ct);
    Task<ComplianceCase?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ComplianceCase>> ListPendingApprovalAsync(CancellationToken ct);

    /// <summary>Resumen ligero de los casos, el más reciente primero; <paramref name="status"/> filtra por estado.</summary>
    Task<IReadOnlyList<CaseSummary>> ListSummariesAsync(CaseStatus? status, CancellationToken ct);
}

/// <summary>Lo mínimo de un caso para pintarlo en una lista.</summary>
public sealed record CaseSummary(
    Guid Id, string Title, string SourceId, DateOnly PublishedOn, CaseStatus Status, int Revision,
    int Findings, int Actions, int PendingData, bool Escalated, DateTimeOffset SavedAt, bool Expensive = false);

/// <summary>Se guardó un caso que alguien más había cambiado después de leerlo (concurrencia optimista).</summary>
public sealed class CaseConflictException(Guid id)
    : Exception($"El caso {id} ha cambiado desde que se leyó; vuelve a cargarlo y repite la operación.");
