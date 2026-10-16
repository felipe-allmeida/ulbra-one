using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.CreateUnitBudget;

/// <summary>Creates a unit's monthly budget over a period — one <see cref="CostCenterUnitBudget"/> cell per
/// competency in the inclusive range, all with the same monthly ceiling. The cost center owns the cells; the
/// unit must be associated with it (<see cref="CostCenterUnit"/>). Ports the former Budgeting CreateBudget.</summary>
public sealed class CreateUnitBudget : IEndpoint
{
    /// <summary>Guardrail on the materialized range: 36 months = 3 exercises. Keeps the inserts bounded.</summary>
    private const int MaxPeriodMonths = 36;

    public sealed record Request
    {
        [SwaggerSchema("Unit the budget belongs to. It must already be linked to the cost center.")]
        public UnitId UnitId { get; set; }

        [SwaggerSchema("First competency of the period (`YYYY-MM`).")]
        [SwaggerExample("2026-01")]
        [NormalizeText] public string StartCompetency { get; set; } = string.Empty;

        /// <summary>Inclusive end of the period. Omitted → a 12-month period (start + 11); equal to start → a single month.</summary>
        [SwaggerSchema("Last competency of the period (`YYYY-MM`), inclusive. Omit for a 12-month period; "
            + "pass the same value as StartCompetency for a single month. Capped at 36 months.")]
        [SwaggerExample("2026-12")]
        [NormalizeText] public string? EndCompetency { get; set; }

        [SwaggerSchema("Ceiling for EACH month of the period, in BRL — not the total. Every cell created "
            + "gets this same value.")]
        [SwaggerExample("15000.00")]
        public decimal MonthlyLimit { get; set; }

        [SwaggerSchema("Whether the created cells count toward the budget. Inactive cells are kept but "
            + "ignored by the ceiling checks and by the annual total.")]
        public bool Active { get; set; } = true;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{costCenterId}/unit-budgets", Handle)
            .WithName("CreateCostCenterUnitBudget")
            .WithTags("Core")
            .WithSummary("Creates a unit's monthly budget over a period.")
            .WithDescription("Materializes one budget cell per competency in the inclusive range, all with "
                + "the same monthly ceiling — so a 12-month period creates 12 cells, each capped at "
                + "MonthlyLimit. The cost center owns the cells and the unit must be linked to it. The "
                + "range is capped at 36 months (three exercises) to keep the insert bounded; a cell that "
                + "already exists for a (unit, competency) returns 409.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .Produces<CostCenterUnitBudgetPeriodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        CostCenterId costCenterId,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == costCenterId.Value, ct);
        if (costCenter is null) return TypedResults.NotFound();
        if (!costCenter.Active)
            return ErrorResults.InvalidRequest("Cost center is inactive.", ErrorCodes.CostCenterUnitBudget.CostCenterInactive);

        var start = req.StartCompetency;
        if (!CompetencyFormat.IsValid(start)) return CompetencyInvalid(start);

        // Default the period to 12 months (start + 11) when no end is given; end == start = a single month.
        var end = string.IsNullOrWhiteSpace(req.EndCompetency) ? CompetencyFormat.AddMonths(start, 11) : req.EndCompetency;
        if (!CompetencyFormat.IsValid(end)) return CompetencyInvalid(end);
        if (CompetencyFormat.Compare(end, start) < 0)
            return ErrorResults.InvalidRequest($"EndCompetency '{end}' cannot precede StartCompetency '{start}'.", ErrorCodes.CostCenterUnitBudget.CompetencyRangeInvalid);
        if (CompetencyFormat.CountMonths(start, end) > MaxPeriodMonths)
            return ErrorResults.InvalidRequest($"The period cannot span more than {MaxPeriodMonths} months.", ErrorCodes.CostCenterUnitBudget.CompetencyHorizonExceeded);

        if (req.MonthlyLimit < 0)
            return ErrorResults.InvalidRequest("MonthlyLimit cannot be negative.", ErrorCodes.CostCenterUnitBudget.LimitInvalid);

        // The unit must belong to the cost center's company AND be associated with it (CostCenterUnit).
        var unit = await uow.ReadSet<Unit>().FirstOrDefaultAsync(u => u.Id == req.UnitId.Value, ct);
        if (unit is null || unit.CompanyId != costCenter.CompanyId)
            return ErrorResults.InvalidRequest("Unit not found for this company.", ErrorCodes.CostCenterUnitBudget.UnitInvalid);
        var associated = await uow.ReadSet<CostCenterUnit>().AnyAsync(x => x.CostCenterId == costCenterId.Value && x.UnitId == req.UnitId.Value, ct);
        if (!associated)
            return ErrorResults.InvalidRequest("Unit is not associated with this cost center.", ErrorCodes.CostCenterUnitBudget.UnitNotInCostCenter);

        var competencies = CompetencyFormat.EnumerateRange(start, end).ToList();

        var conflicts = await ConflictingCompetencies(uow, costCenterId.Value, req.UnitId.Value, start, end, ct);
        if (conflicts.Count > 0) return DuplicateInPeriod(conflicts);

        var cells = new List<CostCenterUnitBudget>(competencies.Count);
        foreach (var competency in competencies)
        {
            if (!CompetencyFormat.IsValid(competency)) return CompetencyInvalid(competency);
            var cell = new CostCenterUnitBudget(costCenterId.Value, req.UnitId.Value, costCenter.CompanyId, competency, req.MonthlyLimit, req.Active);
            await uow.AddAsync(cell, ct);
            cells.Add(cell);
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            uow.ClearChangeTracker();
            var raced = await ConflictingCompetencies(uow, costCenterId.Value, req.UnitId.Value, start, end, ct);
            return DuplicateInPeriod(raced.Count > 0 ? raced : competencies);
        }

        return TypedResults.Created(
            $"/api/cost-centers/{costCenterId}/unit-budgets?unitId={req.UnitId}&competencyFrom={start}&competencyTo={end}",
            CostCenterUnitBudgetPeriodResponse.From(start, end, cells));
    }

    private static async Task<List<string>> ConflictingCompetencies(
        IUnitOfWork uow, Guid costCenterId, Guid unitId, string start, string end, CancellationToken ct) =>
        await uow.ReadSet<CostCenterUnitBudget>()
            .Where(o => o.CostCenterId == costCenterId && o.UnitId == unitId
                        && string.Compare(o.Competency, start) >= 0 && string.Compare(o.Competency, end) <= 0)
            .Select(o => o.Competency)
            .ToListAsync(ct);

    private static IResult CompetencyInvalid(string value) =>
        ErrorResults.InvalidRequest($"Competency '{value}' is not a valid \"YYYY-MM\" value.", ErrorCodes.CostCenterUnitBudget.CompetencyInvalid);

    private static IResult DuplicateInPeriod(IReadOnlyList<string> months) =>
        ErrorResults.InvalidState($"A budget already exists for: {string.Join(", ", months.OrderBy(m => m, StringComparer.Ordinal))}.", ErrorCodes.CostCenterUnitBudget.DuplicateInPeriod);
}
