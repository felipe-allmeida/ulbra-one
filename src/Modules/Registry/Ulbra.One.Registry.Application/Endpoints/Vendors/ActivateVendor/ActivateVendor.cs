using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Results;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.ActivateVendor;

public sealed class ActivateVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/activate", Handle)
            .WithName("ActivateVendor")
            .WithTags("Registry")
            .RequireAuthorization()
            .Produces<VendorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        [FromKeyedServices("registry")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = await uow.Set<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        if (vendor is null)
            return Result.Failure<VendorResponse>(Error.NotFound("Vendor.NotFound", "Vendor not found.")).ToOk();
        vendor.Activate();
        await uow.SaveChangesAsync(ct);
        return Result.Success(VendorResponse.From(vendor)).ToOk();
    }
}
