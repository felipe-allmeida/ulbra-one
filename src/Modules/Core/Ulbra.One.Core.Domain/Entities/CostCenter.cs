namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Cost center — functional dimension (Marketing, IT, HR…) that belongs to a
/// <see cref="Company"/> and is cross-cutting: it operates across N units (see <see cref="CostCenterUnit"/>).</summary>
public class CostCenter
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal AnnualBudget { get; private set; }

    /// <summary>Expenses up to this amount are approved automatically. 0 = nothing auto-approves.</summary>
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
