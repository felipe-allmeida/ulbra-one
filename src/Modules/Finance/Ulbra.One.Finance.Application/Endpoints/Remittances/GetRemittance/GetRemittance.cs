using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.GetRemittance;

public sealed class GetRemittance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle)
            .WithName("GetRemittance").WithTags("Finance").RequirePermission(Permissions.TreasuryRead)
            .Produces<RemittanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(RemittanceId id, IUnitOfWork uow, CancellationToken ct)
    {
        // Lines are AutoIncluded by the EF configuration
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (remittance is null)
            return TypedResults.Problem(detail: "Remittance not found.", statusCode: 404, title: "Remittance.NotFound");

        return TypedResults.Ok(RemittanceResponse.From(remittance));
    }
}
