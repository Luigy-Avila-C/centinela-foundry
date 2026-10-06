using System.Text.Json;
using System.Text.Json.Serialization;
using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Infrastructure.Persistence;

/// <summary>Opciones de serialización compartidas por todas las implementaciones del repositorio.</summary>
public static class CaseJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };
}

/// <summary>
/// Forma en que se guarda un caso en Cosmos DB. Los campos de arriba están desnormalizados a propósito para poder
/// listar los casos sin leer los documentos enteros.
/// </summary>
public sealed record CaseDocument(
    string Id,
    string DocType,
    int SchemaVersion,
    string Title,
    string SourceId,
    DateOnly PublishedOn,
    CaseStatus Status,
    int Revision,
    int Findings,
    int Actions,
    int PendingData,
    bool Escalated,
    DateTimeOffset SavedAt,
    CaseSnapshot Snapshot,
    bool Expensive = false)
{
    public const string Type = "case";
    public const int CurrentSchema = 1;

    public static CaseDocument From(ComplianceCase c, DateTimeOffset now)
    {
        var s = Compact(c.ToSnapshot());
        return new(
            s.Id.ToString(), Type, CurrentSchema, s.Change.Title, s.Change.SourceId, s.Change.PublishedOn, s.Status, s.Revision,
            s.Findings.Count, s.Actions.Count, s.Actions.Sum(a => a.PendingData?.Count ?? 0),
            s.LastVerdict is { Passed: false }, now, s,
            // «Caro»: el caso llegó a gastar análisis, redacción o auditoría. Un fallo del guardián o un descarte barato no cuentan.
            Expensive: s.Analysis is not null || (s.Status == CaseStatus.Failed && !s.Log.Any(l => l.Contains("Bloqueado por el guardián"))));
    }

    public CaseSummary ToSummary() => new(
        Guid.Parse(Id), Title, SourceId, PublishedOn, Status, Revision, Findings, Actions, PendingData, Escalated, SavedAt, Expensive);

    public ComplianceCase ToCase() => ComplianceCase.Restore(Snapshot);

    /// <summary>
    /// Guardar la norma entera con cada caso sería pesado (cientos de KB) y no aporta: el texto íntegro se vuelve a pedir al
    /// BOE por su identificador. Se conservan los fragmentos que el caso cita, para poder mostrar la evidencia.
    /// </summary>
    public static CaseSnapshot Compact(CaseSnapshot s)
    {
        var cited = new HashSet<string>(StringComparer.Ordinal);
        if (s.Analysis is { } a)
        {
            cited.UnionWith(a.Citations);
            foreach (var claim in a.Claims ?? []) cited.UnionWith(claim.Citations);
        }

        foreach (var f in s.Findings) cited.UnionWith(f.NormCitations ?? []);
        foreach (var x in s.Actions) cited.UnionWith(x.NormCitations ?? []);

        var sections = s.Change.Sections?.Where(c => cited.Contains(c.Id)).ToList();
        return s with { Change = s.Change with { RawText = "", Sections = sections } };
    }
}
