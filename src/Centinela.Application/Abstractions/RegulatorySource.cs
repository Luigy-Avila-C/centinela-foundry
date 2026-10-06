using Centinela.Domain;

namespace Centinela.Application;

/// <summary>Una publicación oficial del sumario de una fuente, todavía sin descargar entera.</summary>
public sealed record SourceEntry(
    string SourceId, string SourceName, string Title, string Department, string Section, Uri Url, DateOnly Date);

/// <summary>Una fuente oficial que se vigila (hoy el BOE). Abstraída para poder probar el vigilante sin red.</summary>
public interface IRegulatorySource
{
    Task<IReadOnlyList<SourceEntry>> GetEntriesAsync(DateOnly date, CancellationToken ct);

    /// <summary>Descarga el texto íntegro y lo trocea en fragmentos citables.</summary>
    Task<RegulatoryChange> GetFullChangeAsync(SourceEntry entry, CancellationToken ct);
}
