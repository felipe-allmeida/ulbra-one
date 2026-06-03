namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Centro de custo — dimensão funcional (Marketing, TI, RH…) que pertence a uma
/// <see cref="Company"/> e é transversal: atua em N unidades (ver <see cref="CostCenterUnit"/>).</summary>
public class CostCenter
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal AnnualBudget { get; private set; }

    /// <summary>Despesas até este valor são aprovadas automaticamente. 0 = nada auto-aprova.</summary>
    public decimal AutoApprovalLimit { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private CostCenter() { }

    public CostCenter(Guid companyId, string code, string name, decimal annualBudget, decimal autoApprovalLimit = 0)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        AnnualBudget = annualBudget;
        AutoApprovalLimit = autoApprovalLimit;
    }

    public void Update(string code, string name, decimal annualBudget, decimal autoApprovalLimit)
    {
        Code = code;
        Name = name;
        AnnualBudget = annualBudget;
        AutoApprovalLimit = autoApprovalLimit;
    }
}
