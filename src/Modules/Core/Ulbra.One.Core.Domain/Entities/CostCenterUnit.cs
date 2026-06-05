namespace Ulbra.One.Core.Domain.Entities;

/// <summary>N:N association between <see cref="CostCenter"/> and <see cref="Unit"/> —
/// which units each cost center operates in.</summary>
public class CostCenterUnit
{
    public Guid CostCenterId { get; private set; }
    public Guid UnitId { get; private set; }

    private CostCenterUnit() { }

    public CostCenterUnit(Guid costCenterId, Guid unitId)
    {
        CostCenterId = costCenterId;
        UnitId = unitId;
    }
}
