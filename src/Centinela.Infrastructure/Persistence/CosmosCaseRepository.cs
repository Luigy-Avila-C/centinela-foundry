using System.Net;
using System.Runtime.CompilerServices;
using Azure.Identity;
using Centinela.Application;
using Centinela.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Centinela.Infrastructure.Persistence;

public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    /// <summary>Endpoint de la cuenta (https://….documents.azure.com:443/). Vacío = sin Cosmos (memoria).</summary>
    public string Endpoint { get; set; } = "";

    public string Database { get; set; } = "centinela";
    public string Container { get; set; } = "cases";
}

/// <summary>
/// Casos en Azure Cosmos DB, con Microsoft Entra ID (sin claves). Clave de partición <c>/id</c>: las lecturas por caso son
/// puntuales y el volumen es bajo, así que las listas (entre particiones) no importan. Usa el ETag del documento para
/// que dos revisores no se pisen: el segundo en guardar recibe <see cref="CaseConflictException"/>.
/// La base de datos y el contenedor los crea la infraestructura (Bicep); con RBAC de datos el código no puede crearlos.
/// </summary>
public sealed class CosmosCaseRepository : ICaseRepository, IDisposable
{
    private readonly CosmosClient _client;
    private readonly Container _container;
    private readonly ConditionalWeakTable<ComplianceCase, string> _etags = new();
    private readonly TimeProvider _clock;

    public CosmosCaseRepository(IOptions<CosmosOptions> options, TimeProvider? clock = null)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Endpoint)) throw new InvalidOperationException("Falta Cosmos:Endpoint.");

        _clock = clock ?? TimeProvider.System;
        _client = new CosmosClient(o.Endpoint, new DefaultAzureCredential(), new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = CaseJson.Options,
            ConnectionMode = ConnectionMode.Gateway,
            ApplicationName = "centinela-foundry",
        });
        _container = _client.GetContainer(o.Database, o.Container);
    }

    public async Task SaveAsync(ComplianceCase c, CancellationToken ct)
    {
        var doc = CaseDocument.From(c, _clock.GetUtcNow());
        var key = new PartitionKey(doc.Id);

        try
        {
            ItemResponse<CaseDocument> response;
            if (_etags.TryGetValue(c, out var etag))
            {
                response = await _container.ReplaceItemAsync(doc, doc.Id, key, new ItemRequestOptions { IfMatchEtag = etag }, ct);
            }
            else
            {
                // Un caso nuevo: si ya existe el id, alguien lo guardó antes (conflicto).
                response = await _container.CreateItemAsync(doc, key, cancellationToken: ct);
            }

            _etags.AddOrUpdate(c, response.ETag);
        }
        catch (CosmosException e) when (e.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            throw new CaseConflictException(c.Id);
        }
    }

    public async Task<ComplianceCase?> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<CaseDocument>(id.ToString(), new PartitionKey(id.ToString()), cancellationToken: ct);
            var c = response.Resource.ToCase();
            _etags.AddOrUpdate(c, response.ETag);
            return c;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ComplianceCase>> ListPendingApprovalAsync(CancellationToken ct)
    {
        var summaries = await ListSummariesAsync(CaseStatus.AwaitingHumanApproval, ct);

        // El ETag solo llega en una lectura puntual, no en una consulta: se relee cada caso.
        var cases = new List<ComplianceCase>();
        foreach (var s in summaries)
        {
            var c = await GetAsync(s.Id, ct);
            if (c is not null) cases.Add(c);
        }

        return cases;
    }

    public async Task<IReadOnlyList<CaseSummary>> ListSummariesAsync(CaseStatus? status, CancellationToken ct)
    {
        // Solo los campos desnormalizados: no se transfiere el snapshot.
        var sql = "SELECT c.id, c.title, c.sourceId, c.publishedOn, c.status, c.revision, c.findings, c.actions, " +
                  "c.pendingData, c.escalated, c.savedAt, c.expensive FROM c WHERE c.docType = @t" +
                  (status is null ? "" : " AND c.status = @s") + " ORDER BY c.savedAt DESC";
        var query = new QueryDefinition(sql).WithParameter("@t", CaseDocument.Type);
        if (status is not null) query = query.WithParameter("@s", status.Value.ToString());

        var summaries = new List<CaseSummary>();
        using var iterator = _container.GetItemQueryIterator<SummaryRow>(query);
        while (iterator.HasMoreResults)
        {
            foreach (var r in await iterator.ReadNextAsync(ct))
            {
                summaries.Add(new(Guid.Parse(r.Id), r.Title, r.SourceId, r.PublishedOn, r.Status, r.Revision,
                    r.Findings, r.Actions, r.PendingData, r.Escalated, r.SavedAt, r.Expensive));
            }
        }

        return summaries;
    }

    public void Dispose() => _client.Dispose();

    private sealed record SummaryRow(
        string Id, string Title, string SourceId, DateOnly PublishedOn, CaseStatus Status, int Revision,
        int Findings, int Actions, int PendingData, bool Escalated, DateTimeOffset SavedAt, bool Expensive = false);
}
