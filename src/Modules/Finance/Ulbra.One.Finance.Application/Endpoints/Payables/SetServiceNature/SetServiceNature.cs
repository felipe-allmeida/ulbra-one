using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetServiceNature;

/// <summary>Marks whether this payable is a service subject to source withholding (true), a product / not
/// subject (false), or undetermined (null). This — not the LC 116 subitem — is what triggers the automatic
/// withholding; setting it recomputes immediately (no-op when the payable is already scheduled/locked).</summary>
public sealed class SetServiceNature : IEndpoint
{
    public sealed record Request
    {
        public bool? IsService { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/service-nature", Handle)
            .WithName("SetPayableServiceNature").WithTags("Finance")
            .RequireAuthorization()
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id, Request req, IUnitOfWork uow, IAutoWithholdingService autoWithholding, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        try
        {
            p.SetIsService(req.IsService);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "Payable.NotMutable");
        }

        // Recompute so the nature change takes effect immediately (no-op when scheduled/locked or not a service).
        await autoWithholding.RecalculateAndApplyAsync(p, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
