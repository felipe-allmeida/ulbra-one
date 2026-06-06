using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.ListEmployees;

public sealed class ListEmployees : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListEmployees")
            .WithTags("Registry")
            .RequireAuthorization()
            .Produces<PaginatedResponse<EmployeeResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        string? search,
        [AsParameters] PaginationQueryParameters pagination,
        [FromKeyedServices("registry")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<EmployeeEntity>();
        if (companyId is { } cid) query = query.Where(e => e.CompanyId == cid.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Name.Contains(search) || e.Document.Contains(search));
        var page = await query.OrderBy(e => e.Name).ToPaginatedAsync(pagination, ct);
        return Results.Ok(page.Map(EmployeeResponse.From));
    }
}
