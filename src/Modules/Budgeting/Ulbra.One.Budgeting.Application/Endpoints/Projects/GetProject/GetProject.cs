using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProject;

public sealed class GetProject : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetProject")
            .WithTags("Budgeting")
            .WithSummary("Gets a project.")
            .WithDescription("Returns a single project by id. For its budget tracking figures use "
                + "GET /api/projects/{id}/metrics instead.")
            .RequirePermission(Permissions.ProjectsRead)
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(ProjectId id, IUnitOfWork uow, CancellationToken ct)
    {
        var project = await uow.ReadSet<Project>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        return project is null
            ? ErrorResults.NotFound("Project not found.", ErrorCodes.Project.NotFound)
            : TypedResults.Ok(ProjectResponse.From(project));
    }
}
