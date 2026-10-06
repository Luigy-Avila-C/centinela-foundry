using Centinela.Application;
using Centinela.Domain;

namespace Centinela.Api;

/// <summary>Endpoints de casos: listar, ver el detalle, aprobar y rechazar. Las reglas de negocio son del dominio, no de la API.</summary>
public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app, PricingOptions pricing)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/cases", async (string? status, ICaseRepository repository, CancellationToken ct) =>
        {
            if (status is not null && !Enum.TryParse<CaseStatus>(status, ignoreCase: true, out _))
            {
                return Results.BadRequest(new { error = $"Estado desconocido: {status}." });
            }

            CaseStatus? filter = status is null ? null : Enum.Parse<CaseStatus>(status, ignoreCase: true);
            return Results.Ok(await repository.ListSummariesAsync(filter, ct));
        });

        api.MapGet("/cases/{id:guid}", async (Guid id, ICaseRepository repository, CancellationToken ct) =>
            await repository.GetAsync(id, ct) is { } c
                ? Results.Ok(CaseDetailDto.From(c, pricing))
                : Results.NotFound(new { error = "No existe ese caso." }));

        api.MapPost("/cases/{id:guid}/approve", (Guid id, ApproveRequest body, ICaseRepository repository, CancellationToken ct) =>
            DecideAsync(id, repository, pricing, ct, c => c.Approve(body.Reviewer ?? "", body.AcknowledgeRisks)));

        api.MapPost("/cases/{id:guid}/reject", (Guid id, RejectRequest body, ICaseRepository repository, CancellationToken ct) =>
            DecideAsync(id, repository, pricing, ct, c =>
            {
                if (string.IsNullOrWhiteSpace(body.Reviewer)) throw new ArgumentException("Hace falta el nombre de quien rechaza.");
                if (string.IsNullOrWhiteSpace(body.Comment)) throw new ArgumentException("Hace falta explicar por qué se rechaza.");
                c.Reject(body.Reviewer, body.Comment);
            }));

        return app;
    }

    // Carga el caso, aplica la decisión y lo guarda con control de concurrencia (ETag): dos revisores no se pisan.
    private static async Task<IResult> DecideAsync(
        Guid id, ICaseRepository repository, PricingOptions pricing, CancellationToken ct, Action<ComplianceCase> decision)
    {
        var c = await repository.GetAsync(id, ct);
        if (c is null) return Results.NotFound(new { error = "No existe ese caso." });

        try
        {
            decision(c);
            await repository.SaveAsync(c, ct);
            return Results.Ok(CaseDetailDto.From(c, pricing));
        }
        catch (RiskNotAcknowledgedException e)
        {
            return Results.UnprocessableEntity(new { error = e.Message, risks = e.Risks });
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
        catch (Exception e) when (e is InvalidOperationException or CaseConflictException)
        {
            return Results.Conflict(new { error = e.Message });
        }
    }
}
