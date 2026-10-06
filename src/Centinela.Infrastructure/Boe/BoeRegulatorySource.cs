using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Infrastructure.Boe;

/// <summary>El BOE como fuente vigilada: secciones I y III del sumario diario y texto íntegro en XML.</summary>
public sealed class BoeRegulatorySource(BoeClient boe) : IRegulatorySource
{
    public async Task<IReadOnlyList<SourceEntry>> GetEntriesAsync(DateOnly date, CancellationToken ct) =>
        (await boe.GetSummaryAsync(date, BoeClient.CompanySections, ct))
            .Select(e => new SourceEntry(e.Id, "BOE", e.Title, e.Department, e.Section, e.HtmlUrl, date))
            .ToList();

    public async Task<RegulatoryChange> GetFullChangeAsync(SourceEntry entry, CancellationToken ct)
    {
        var document = await boe.GetDocumentAsync(entry.SourceId, ct);
        return new RegulatoryChange(
            document.Id, entry.SourceName, document.Title, document.Url, document.PublishedOn,
            string.Join("\n", document.Paragraphs.Select(p => p.Text)),
            DocumentChunker.Chunk(document));
    }
}
