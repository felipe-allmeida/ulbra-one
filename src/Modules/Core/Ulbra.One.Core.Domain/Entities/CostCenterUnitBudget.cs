using Ulbra.One.Common.Domain.Auditing;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;

namespace Ulbra.One.Core.Domain.Entities;

/// <summary>
/// Monthly budget ceiling for a single <b>CostCenter × Unit × Competency</b> cell — the source of truth
/// for a cost center's budget (relocated here from the former Budgeting <c>Budget</c>). The
/// <see cref="CostCenter"/> owns these cells: its "annual budget" is the roll-up (Σ <see cref="MonthlyLimit"/>)
/// of the cells of its associated units. Enforcement stays per month; a create-by-period materializes one
/// cell per competency over a range (e.g. Jan–Jul). The dimensions form the unique key and are immutable
/// after creation — changing them is delete + create; <see cref="Update"/> only changes the ceiling and the
/// active flag.
/// </summary>
public sealed class CostCenterUnitBudget : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Owning cost center (intra-Core FK).</summary>
    public Guid CostCenterId { get; private set; }

    /// <summary>Unit the cell budgets (intra-Core FK); must be associated with the cost center via
    /// <see cref="CostCenterUnit"/>.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Company the cell belongs to — denormalized from the cost center/unit for scoping + indexing.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Competency month in <c>"YYYY-MM"</c> form (validated, zero-padded).</summary>
    public string Competency { get; private set; } = string.Empty;

    /// <summary>Approved monthly ceiling for the cell. Never negative; zero = no spend allowed.</summary>
    public decimal MonthlyLimit { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private CostCenterUnitBudget() { }

    public CostCenterUnitBudget(Guid costCenterId, Guid unitId, Guid companyId, string competency, decimal monthlyLimit, bool active = true)
    {
        if (costCenterId == Guid.Empty) throw new ArgumentException("CostCenterId is required.", nameof(costCenterId));
        if (unitId == Guid.Empty) throw new ArgumentException("UnitId is required.", nameof(unitId));
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));

        competency = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(competency))
            throw new ArgumentException($"Competency '{competency}' is not a valid \"YYYY-MM\" value.", nameof(competency));

        CostCenterId = costCenterId;
        UnitId = unitId;
        CompanyId = companyId;
        Competency = competency;
        MonthlyLimit = NormalizeLimit(monthlyLimit);
        Active = active;
    }

    /// <summary>Updates the ceiling and active flag. Dimensions (cost center/unit/competency) are immutable.</summary>
    public void Update(decimal monthlyLimit, bool active)
    {
        MonthlyLimit = NormalizeLimit(monthlyLimit);
        Active = active;
    }

    /// <summary>Raises the ceiling by <paramref name="delta"/> — an approved supplement (Suplementação) or the
    /// destination of a transfer (Transferência). Explicit intent for a budget adjustment.</summary>
    public void Increase(decimal delta)
    {
        if (delta <= 0) throw new ArgumentOutOfRangeException(nameof(delta), "Delta must be greater than zero.");
        MonthlyLimit = NormalizeLimit(MonthlyLimit + delta);
    }

    /// <summary>Lowers the ceiling by <paramref name="delta"/> — an approved reduction (Redução) or the source
    /// of a transfer. Never drives the ceiling below zero: throws <see cref="InvalidOperationException"/> as a
    /// defensive net (the budget-adjustment mutator pre-checks and maps the case to a 409).</summary>
    public void Decrease(decimal delta)
    {
        if (delta <= 0) throw new ArgumentOutOfRangeException(nameof(delta), "Delta must be greater than zero.");
        var next = decimal.Round(MonthlyLimit - delta, 2, MidpointRounding.AwayFromZero);
        if (next < 0) throw new InvalidOperationException("MonthlyLimit cannot go negative.");
        MonthlyLimit = next;
    }

    private static decimal NormalizeLimit(decimal monthlyLimit)
    {
        if (monthlyLimit < 0) throw new ArgumentOutOfRangeException(nameof(monthlyLimit), "MonthlyLimit cannot be negative.");
        return decimal.Round(monthlyLimit, 2, MidpointRounding.AwayFromZero);
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
