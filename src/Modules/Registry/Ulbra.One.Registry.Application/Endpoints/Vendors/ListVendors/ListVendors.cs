using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.ListVendors;

public sealed class ListVendors : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListVendors")
            .WithTags("Registry")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<PaginatedResponse<VendorResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        string? search,
        [AsParameters] PaginationQueryParameters pagination,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<VendorEntity>();
        if (companyId is { } cid) query = query.Where(v => v.CompanyId == cid.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(v => v.Name.Contains(search) || v.Document.Contains(search));
        var page = await query.OrderBy(v => v.Name).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(VendorResponse.From));
    }
}
