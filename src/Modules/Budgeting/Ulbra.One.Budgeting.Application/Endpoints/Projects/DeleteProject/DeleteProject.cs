using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.DeleteProject;

public sealed class DeleteProject : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteProject")
            .WithTags("Budgeting")
            .WithSummary("Deletes a project.")
            .WithDescription("Hard-deletes a project that no requisition points at. A project with linked "
                + "requisitions is history rather than disposable configuration, so it returns 409 — cancel "
                + "it through PUT /api/projects/{id} instead.")
            .RequirePermission(Permissions.ProjectsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        ProjectId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var project = await uow.Set<Project>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (project is null) return TypedResults.NotFound();

        // A project that already carries requisitions is history, not disposable config: deleting it
        // would orphan their ProjectId (loose Guid, no FK to cascade). Cancel it instead.
        var hasRequisitions = await uow.ReadSet<Requisition>().AnyAsync(r => r.ProjectId == id.Value, ct);
        if (hasRequisitions)
            return ErrorResults.InvalidState("This project has linked requisitions; cancel it instead of deleting.", ErrorCodes.Project.HasRequisitions);

        uow.Remove(project);
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
