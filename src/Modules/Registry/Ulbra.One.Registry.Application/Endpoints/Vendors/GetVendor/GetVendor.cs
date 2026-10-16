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

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.GetVendor;

public sealed class GetVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetVendor")
            .WithTags("Registry")
            .WithSummary("Gets a vendor.")
            .WithDescription("Returns a single vendor by id, including its address and every registered detail. "
                + "Vendors are global to the organization: one registration is payable by every "
                + "company.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<VendorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        return (vendor is null
            ? Result.Failure<VendorResponse>(Error.NotFound(ErrorCodes.Vendor.NotFound, "Vendor not found."))
            : Result.Success(VendorResponse.From(vendor))).ToOk();
    }
}
