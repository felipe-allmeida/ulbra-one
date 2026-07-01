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

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetSimplesDeclaration;

/// <summary>Marks whether the vendor's Simples Nacional declaration is on file — the condition for waiving
/// federal withholding (IN RFB 1.234/2012, art. 4, XI). Without it, the paying source must withhold.</summary>
public sealed class SetSimplesDeclaration : IEndpoint
{
    public sealed record Request
    {
        public bool Provided { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/simples-declaration", Handle)
            .WithName("SetSimplesDeclaration").WithTags("Finance")
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
            p.SetSimplesDeclarationProvided(req.Provided);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 400, title: "Payable.NotMutable");
        }

        // The declaration flips the federal exemption → recompute so the change takes effect immediately
        // (no-op when the payable is already scheduled/locked or is not a service).
        await autoWithholding.RecalculateAndApplyAsync(p, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
