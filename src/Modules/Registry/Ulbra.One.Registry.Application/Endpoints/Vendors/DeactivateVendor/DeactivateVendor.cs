using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.DeactivateVendor;

public sealed class DeactivateVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/deactivate", Handle)
            .WithName("DeactivateVendor")
            .WithTags("Registry")
            .WithSummary("Deactivates a vendor.")
            .WithDescription("Marks the vendor as out of use without deleting it: it stays visible and keeps its "
                + "history. Idempotent.")
            .RequirePermission(Permissions.RegistryWrite)
            .Produces<VendorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = await uow.Set<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        if (vendor is null)
            return Result.Failure<VendorResponse>(Error.NotFound(ErrorCodes.Vendor.NotFound, "Vendor not found.")).ToOk();
        vendor.Deactivate();
        await uow.SaveChangesAsync(ct);
        return Result.Success(VendorResponse.From(vendor)).ToOk();
    }
}
