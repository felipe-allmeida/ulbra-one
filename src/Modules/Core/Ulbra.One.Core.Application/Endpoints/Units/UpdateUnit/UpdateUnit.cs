using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Units.UpdateUnit;

public sealed class UpdateUnit : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string Name { get; set; } = string.Empty;
        [NormalizeText] public string Acronym { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateUnit")
            .WithTags("Core")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .Produces<UnitResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        UnitId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var unit = await uow.Set<Unit>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (unit is null) return TypedResults.NotFound();
        unit.Update(req.Name, req.Acronym);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(UnitResponse.From(unit));
    }
}
