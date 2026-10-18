using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.CreateBudgetAdjustment;

/// <summary>Creates a budget adjustment (adequação) awaiting triage. Validates the fields and the budget
/// dimensions via the shared lookup (deriving the company from the unit) and assigns a per-company
/// sequential <c>Code</c> = max+1, retrying on the unique-index race — same pattern as CreateRequisition.</summary>
public sealed class CreateBudgetAdjustment : IEndpoint
{
    private const int MaxAttempts = 5;

    public sealed record Request
    {
        [SwaggerSchema("Unit the adjustment belongs to. The company is derived from it.")]
        public UnitId UnitId { get; set; }

        [SwaggerSchema("Cost center whose budget ceiling changes.")]
        public CostCenterId CostCenterId { get; set; }

        [SwaggerSchema("What the adjustment does: Supplementation raises the ceiling, Reduction lowers it, "
            + "Transfer moves budget from SourceCompetency into Competency.")]
        public BudgetAdjustmentType Type { get; set; }

        [SwaggerSchema("Destination accounting competency (`YYYY-MM`) — the month whose ceiling is raised, "
            + "or lowered for a Reduction.")]
        [SwaggerExample("2026-08")]
        [NormalizeText] public string Competency { get; set; } = string.Empty;

        [SwaggerSchema("Source competency (`YYYY-MM`) the budget comes out of. Required for Transfer, "
            + "rejected for the other types.")]
        [SwaggerExample("2026-07")]
        [NormalizeText] public string? SourceCompetency { get; set; }

        [SwaggerSchema("Amount to move, in BRL. Always positive — the direction comes from Type.")]
        [SwaggerExample("25000.00")]
        public decimal Value { get; set; }

        [SwaggerSchema("Why the adjustment is needed. Required — it is what triage and the approver read.")]
        [SwaggerExample("Reallocation of the unspent training budget to cover equipment maintenance.")]
        [NormalizeText] public string Justification { get; set; } = string.Empty;

        [SwaggerSchema("Free-text note on where the offsetting budget comes from (contrapartidas).")]
        [NormalizeText] public string? Counterparts { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateBudgetAdjustment")
            .WithTags("Budgeting")
            .WithSummary("Creates a budget adjustment.")
            .WithDescription("Opens a budget adjustment (adequação) awaiting triage. Validates the fields "
                + "and the budget dimensions, deriving the company from the unit, and assigns a per-company "
                + "sequential code. Nothing moves in the budget yet — the ceiling only changes on approval.")
            .RequirePermission(Permissions.BudgetAdjustmentsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        Request req,
        IBudgetDimensionLookup dimensions,
        IUnitOfWork uow,
        IPublishEndpoint publish,
        TimeProvider clock,
        CancellationToken ct)
    {
        var invalid = BudgetAdjustmentValidation.Validate(req.Type, req.Competency, req.SourceCompetency, req.Value, req.Justification, req.Counterparts);
        if (invalid is not null) return invalid;

        var dimension = await dimensions.ResolveAsync(req.UnitId.Value, req.CostCenterId.Value, ct);
        if (!dimension.IsValid)
            return ErrorResults.InvalidRequest(DimensionDetail(dimension.Status), DimensionTitle(dimension.Status));

        for (var attempt = 0; ; attempt++)
        {
            var nextCode = (await uow.ReadSet<BudgetAdjustment>()
                .Where(a => a.CompanyId == dimension.CompanyId)
                .MaxAsync(a => (int?)a.Code, ct) ?? 0) + 1;

            var budgetAdjustment = new BudgetAdjustment(
                dimension.CompanyId, req.UnitId.Value, req.CostCenterId.Value, nextCode,
                req.Type, req.Competency, req.SourceCompetency, req.Value, req.Justification, req.Counterparts);

            await uow.AddAsync(budgetAdjustment, ct);
            // Synthetic "created" notification event (the adequação is born in AwaitingTriage without a
            // transition row). Published INSIDE the loop: the catch's ClearChangeTracker wipes the outbox
            // row on a Code collision, so re-publishing each iteration keeps it atomic with the winning save.
            await budgetAdjustment.PublishCreated(publish, clock.GetUtcNow(), ct);
            try
            {
                await uow.SaveChangesAsync(ct);
                return TypedResults.Created($"/api/budget-adjustments/{new BudgetAdjustmentId(budgetAdjustment.Id)}", BudgetAdjustmentResponse.From(budgetAdjustment));
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Lost the race for this Code against a concurrent create. Clear the tracker so the next
                // iteration re-computes max+1 with a fresh entity (also discarding this iteration's outbox row).
                uow.ClearChangeTracker();
            }
        }
    }

    // Distinct, stable title codes per dimension failure so the frontend can localize a specific message.
    private static string DimensionTitle(DimensionStatus status) => status switch
    {
        DimensionStatus.UnitNotFound => ErrorCodes.BudgetAdjustment.UnitNotFound,
        DimensionStatus.CostCenterNotFound => ErrorCodes.BudgetAdjustment.CostCenterNotFound,
        DimensionStatus.CompanyMismatch => ErrorCodes.BudgetAdjustment.CompanyMismatch,
        DimensionStatus.CostCenterInactive => ErrorCodes.BudgetAdjustment.CostCenterInactive,
        DimensionStatus.UnitNotInCostCenter => ErrorCodes.BudgetAdjustment.UnitNotInCostCenter,
        _ => ErrorCodes.BudgetAdjustment.DimensionsInvalid,
    };

    private static string DimensionDetail(DimensionStatus status) => status switch
    {
        DimensionStatus.UnitNotFound => "Unit not found.",
        DimensionStatus.CostCenterNotFound => "Cost center not found.",
        DimensionStatus.CompanyMismatch => "Unit and cost center belong to different companies.",
        DimensionStatus.CostCenterInactive => "Cost center is inactive.",
        DimensionStatus.UnitNotInCostCenter => "Unit is not associated with the selected cost center.",
        _ => "Invalid budget dimensions.",
    };
}
