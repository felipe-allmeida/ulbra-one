using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
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

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.UpdateBudgetAdjustment;

/// <summary>Updates the editable fields of an adjustment (dimensions, code and status are immutable).
/// Allowed only while still awaiting triage — otherwise 409.</summary>
public sealed class UpdateBudgetAdjustment : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("What the adjustment does: Supplementation raises the ceiling, Reduction lowers it, "
            + "Transfer moves budget from SourceCompetency into Competency.")]
        public BudgetAdjustmentType Type { get; set; }

        [SwaggerSchema("Destination accounting competency (`YYYY-MM`).")]
        [SwaggerExample("2026-08")]
        [NormalizeText] public string Competency { get; set; } = string.Empty;

        [SwaggerSchema("Source competency (`YYYY-MM`). Required for Transfer, rejected for the other types.")]
        [SwaggerExample("2026-07")]
        [NormalizeText] public string? SourceCompetency { get; set; }

        [SwaggerSchema("Amount to move, in BRL. Always positive — the direction comes from Type.")]
        [SwaggerExample("25000.00")]
        public decimal Value { get; set; }

        [SwaggerSchema("Why the adjustment is needed. Required.")]
        [SwaggerExample("Reallocation of the unspent training budget to cover equipment maintenance.")]
        [NormalizeText] public string Justification { get; set; } = string.Empty;

        [SwaggerSchema("Free-text note on where the offsetting budget comes from (contrapartidas).")]
        [NormalizeText] public string? Counterparts { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateBudgetAdjustment")
            .WithTags("Budgeting")
            .WithSummary("Updates a budget adjustment.")
            .WithDescription("Replaces the editable fields of an adjustment. Its dimensions (unit, cost "
                + "center), code and status are immutable, and editing is only allowed while it is still "
                + "awaiting triage — once it has advanced, this returns 409.")
            .RequirePermission(Permissions.BudgetAdjustmentsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var budgetAdjustment = await uow.Set<BudgetAdjustment>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        var invalid = BudgetAdjustmentValidation.Validate(req.Type, req.Competency, req.SourceCompetency, req.Value, req.Justification, req.Counterparts);
        if (invalid is not null) return invalid;

        try
        {
            budgetAdjustment.Update(req.Type, req.Competency, req.SourceCompetency, req.Value, req.Justification, req.Counterparts);
        }
        catch (BudgetAdjustmentException ex)
        {
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
