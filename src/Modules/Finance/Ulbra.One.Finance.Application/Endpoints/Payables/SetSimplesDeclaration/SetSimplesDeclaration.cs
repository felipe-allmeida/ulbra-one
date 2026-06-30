using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.SetSimplesDeclaration;

/// <summary>Marca se a declaração do Simples Nacional do fornecedor está em arquivo — condição da dispensa
/// da retenção federal (IN RFB 1.234/2012, art. 4º, XI). Sem ela, a fonte pagadora deve reter.</summary>
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

    private static async Task<IResult> Handle(PayableId id, Request req, IUnitOfWork uow, CancellationToken ct)
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

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
