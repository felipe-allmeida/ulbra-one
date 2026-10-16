using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.ListProjects;

/// <summary>Query-string filters for the project browse list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListProjectsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the projects.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Unit (organizational branch) the projects belong to.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Lifecycle stage to keep.")]
    public ProjectStatus? Status { get; set; }

    [SwaggerSchema("Substring match on the project name. Ignores case.")]
    [SwaggerExample("Campus expansion")]
    public string? Search { get; set; }
}

public sealed class ListProjects : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListProjects")
            .WithTags("Budgeting")
            .WithSummary("Lists projects.")
            .WithDescription("Paginated browse list of budget projects, ordered by name. Every filter is "
                + "optional and combines with AND.")
            .RequirePermission(Permissions.ProjectsRead)
            .Produces<PaginatedResponse<ProjectResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListProjectsRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<Project>();
        if (req.CompanyId is { } cid) query = query.Where(p => p.CompanyId == cid.Value);
        if (req.UnitId is { } uid) query = query.Where(p => p.UnitId == uid.Value);
        if (req.Status is { } s) query = query.Where(p => p.Status == s);
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            // Name match ignores case (lower).
            var term = req.Search.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        var page = await query.OrderBy(p => p.Name).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(ProjectResponse.From));
    }
}
