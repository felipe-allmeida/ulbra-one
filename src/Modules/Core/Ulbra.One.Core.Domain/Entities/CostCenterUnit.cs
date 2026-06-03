namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Associação N:N entre <see cref="CostCenter"/> e <see cref="Unit"/> —
/// em quais unidades cada centro de custo atua.</summary>
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
