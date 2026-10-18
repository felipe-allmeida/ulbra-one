using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.ListEmployees;

/// <summary>Query-string filters for the employee browse list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListEmployeesRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that employs the people. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Free-text search over the employee name and CPF. The name match ignores case; "
        + "the CPF is matched the way it is stored, so a formatted value works.")]
    [SwaggerExample("Maria")]
    public string? Search { get; set; }
}

public sealed class ListEmployees : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListEmployees")
            .WithTags("Registry")
            .WithSummary("Lists employees.")
            .WithDescription("Paginated browse list of employees, ordered by name. Both filters are "
                + "optional and combine with AND. This is registry data — it has no bearing on who can log "
                + "in, which lives in Identity.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<PaginatedResponse<EmployeeResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListEmployeesRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<EmployeeEntity>();
        if (req.CompanyId is { } cid) query = query.Where(e => e.CompanyId == cid.Value);
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            // Name match ignores case (lower); the document is matched the way it is stored
            // (digits CPF; NormalizeCnpj is a no-op for digits).
            var term = req.Search.ToLower();
            var docTerm = DocumentValidator.NormalizeCnpj(req.Search);
            query = query.Where(e =>
                e.Name.ToLower().Contains(term)
                || (docTerm != "" && e.Document.Contains(docTerm)));
        }
        var page = await query.OrderBy(e => e.Name).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(EmployeeResponse.From));
    }
}
