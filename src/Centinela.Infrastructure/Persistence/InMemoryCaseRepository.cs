using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Infrastructure.Persistence;

/// <summary>
/// Repositorio en memoria, para desarrollo y pruebas. Guarda copias (documentos), igual que Cosmos, y aplica la misma
/// concurrencia optimista. Se pierde al cerrar el proceso: no es persistencia, es un sustituto de usar y tirar.
/// </summary>
public sealed class InMemoryCaseRepository(TimeProvider? clock = null) : ICaseRepository
{
    private readonly Dictionary<Guid, (CaseDocument Doc, long Version)> _store = [];
    private readonly Dictionary<ComplianceCase, long> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task SaveAsync(ComplianceCase c, CancellationToken ct)
    {
        lock (_gate)
        {
            var exists = _store.TryGetValue(c.Id, out var current);
            var seen = _seen.GetValueOrDefault(c, -1);
            if (exists && current.Version != seen) throw new CaseConflictException(c.Id);
            if (!exists && seen != -1) throw new CaseConflictException(c.Id);

            var version = (exists ? current.Version : 0) + 1;
            _store[c.Id] = (CaseDocument.From(c, _clock.GetUtcNow()), version);
            _seen[c] = version;
        }

        return Task.CompletedTask;
    }

    public Task<ComplianceCase?> GetAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_store.TryGetValue(id, out var entry)) return Task.FromResult<ComplianceCase?>(null);
            var c = entry.Doc.ToCase();
            _seen[c] = entry.Version;
            return Task.FromResult<ComplianceCase?>(c);
        }
    }

    public Task<IReadOnlyList<ComplianceCase>> ListPendingApprovalAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ComplianceCase>>(_store.Values
                .Where(e => e.Doc.Status == CaseStatus.AwaitingHumanApproval)
                .OrderByDescending(e => e.Doc.SavedAt)
                .Select(e => { var c = e.Doc.ToCase(); _seen[c] = e.Version; return c; })
                .ToList());
        }
    }

    public Task<IReadOnlyList<CaseSummary>> ListSummariesAsync(CaseStatus? status, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CaseSummary>>(_store.Values
                .Where(e => status is null || e.Doc.Status == status)
                .OrderByDescending(e => e.Doc.SavedAt)
                .Select(e => e.Doc.ToSummary())
                .ToList());
        }
    }
}
