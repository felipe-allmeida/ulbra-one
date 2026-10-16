using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.UpdateProject;

public sealed class UpdateProject : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Project name. Must stay unique within the company.")]
        [SwaggerExample("Campus expansion 2026")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Free-text notes about the project's purpose or scope.")]
        [NormalizeText] public string? Description { get; set; }

        [SwaggerSchema("Total budget ceiling for the project, in BRL. Cannot be negative.")]
        [SwaggerExample("450000.00")]
        public decimal BudgetLimit { get; set; }

        [SwaggerSchema("Value consumed before the project was registered here, in BRL. Cannot be negative.")]
        [SwaggerExample("0.00")]
        public decimal InitialConsumedValue { get; set; }

        /// <summary>Optional lifecycle move. Only Active→Finished / →Cancelled are legal.</summary>
        [SwaggerSchema("Optional lifecycle move. Only Active→Finished and Active→Cancelled are legal; "
            + "reopening a closed project is rejected. Omit to leave the status untouched.")]
        public ProjectStatus? Status { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateProject")
            .WithTags("Budgeting")
            .WithSummary("Updates a project.")
            .WithDescription("Replaces the project's editable fields and optionally moves its lifecycle "
                + "status. Every field is required except Description and Status — this is a full replace, "
                + "not a patch.")
            .RequirePermission(Permissions.ProjectsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        ProjectId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.Project.NameRequired);

        if (req.BudgetLimit < 0)
            return ErrorResults.InvalidRequest("BudgetLimit cannot be negative.", ErrorCodes.Project.BudgetLimitInvalid);

        if (req.InitialConsumedValue < 0)
            return ErrorResults.InvalidRequest("InitialConsumedValue cannot be negative.", ErrorCodes.Project.InitialConsumedInvalid);

        var project = await uow.Set<Project>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (project is null)
            return ErrorResults.NotFound("Project not found.", ErrorCodes.Project.NotFound);

        var nameTaken = await uow.ReadSet<Project>()
            .AnyAsync(p => p.CompanyId == project.CompanyId && p.Name == req.Name && p.Id != project.Id, ct);
        if (nameTaken)
            return ErrorResults.InvalidState("A project with this name already exists for the company.", ErrorCodes.Project.Duplicate);

        project.Update(req.Name, req.BudgetLimit, req.InitialConsumedValue, req.Description);

        if (req.Status is { } target && target != project.Status)
        {
            try
            {
                switch (target)
                {
                    case ProjectStatus.Finished: project.Finish(); break;
                    case ProjectStatus.Cancelled: project.Cancel(); break;
                    default:
                        return ErrorResults.InvalidRequest("A project cannot be reopened.", ErrorCodes.Project.InvalidStatusTransition);
                }
            }
            catch (InvalidOperationException ex)
            {
                return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.Project.InvalidStatusTransition);
            }
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ProjectResponse.From(project));
    }
}
