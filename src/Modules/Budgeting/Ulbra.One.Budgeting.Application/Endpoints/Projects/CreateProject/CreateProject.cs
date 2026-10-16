using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Dimensions;
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

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.CreateProject;

public sealed class CreateProject : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Unit the project belongs to. The company is derived from it — a project is never "
            + "assigned to a company directly.")]
        public UnitId UnitId { get; set; }

        [SwaggerSchema("Project name. Must be unique within the company.")]
        [SwaggerExample("Campus expansion 2026")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Free-text notes about the project's purpose or scope.")]
        [NormalizeText] public string? Description { get; set; }

        [SwaggerSchema("Total budget ceiling for the project, in BRL. Cannot be negative.")]
        [SwaggerExample("450000.00")]
        public decimal BudgetLimit { get; set; }

        [SwaggerSchema("Value already consumed before the project was registered here, in BRL. Use it to "
            + "carry an opening balance over from a prior system. Cannot be negative.")]
        [SwaggerExample("0.00")]
        public decimal InitialConsumedValue { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateProject")
            .WithTags("Budgeting")
            .WithSummary("Creates a project.")
            .WithDescription("Registers a budget project under a unit. The name must be unique within the "
                + "unit's company — a collision returns 409, including when it loses a race to a concurrent insert.")
            .RequirePermission(Permissions.ProjectsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        Request req,
        IBudgetDimensionLookup dimensions,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResults.InvalidRequest("Name is required.", ErrorCodes.Project.NameRequired);

        if (req.BudgetLimit < 0)
            return ErrorResults.InvalidRequest("BudgetLimit cannot be negative.", ErrorCodes.Project.BudgetLimitInvalid);

        if (req.InitialConsumedValue < 0)
            return ErrorResults.InvalidRequest("InitialConsumedValue cannot be negative.", ErrorCodes.Project.InitialConsumedInvalid);

        var unit = await dimensions.ResolveUnitAsync(req.UnitId.Value, ct);
        if (!unit.IsValid)
            return ErrorResults.InvalidRequest("Unit not found.", ErrorCodes.Project.UnitNotFound);

        var exists = await uow.ReadSet<Project>()
            .AnyAsync(p => p.CompanyId == unit.CompanyId && p.Name == req.Name, ct);
        if (exists) return Duplicate();

        var project = new Project(unit.CompanyId, req.UnitId.Value, req.Name, req.BudgetLimit, req.InitialConsumedValue, req.Description);
        await uow.AddAsync(project, ct);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race against a concurrent insert of the same (company, name).
            return Duplicate();
        }

        return TypedResults.Created($"/api/projects/{new ProjectId(project.Id)}", ProjectResponse.From(project));
    }

    private static IResult Duplicate() =>
        ErrorResults.InvalidState("A project with this name already exists for the company.", ErrorCodes.Project.Duplicate);
}
