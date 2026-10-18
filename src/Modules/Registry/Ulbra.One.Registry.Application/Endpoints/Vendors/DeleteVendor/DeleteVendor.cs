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
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.DeleteVendor;

public sealed class DeleteVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteVendor")
            .WithTags("Registry")
            .WithSummary("Deletes a vendor.")
            .WithDescription("Soft-deletes the vendor: the row is kept and stamped, so documents that already "
                + "reference it still resolve the name, and it simply stops appearing in the list. To "
                + "take a vendor out of use while keeping the record visible, deactivate it instead.")
            .RequirePermission(Permissions.RegistryWrite)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var vendor = await uow.Set<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        if (vendor is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Vendor.NotFound, "Vendor not found.")).ToOk();
        vendor.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return Result.Success().ToOk();
    }
}
