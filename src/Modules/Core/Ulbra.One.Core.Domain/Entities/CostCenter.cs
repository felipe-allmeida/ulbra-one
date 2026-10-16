using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Core.Domain.Entities;

/// <summary>Cost center — functional dimension (Marketing, IT, HR…) that belongs to a
/// <see cref="Company"/> and is cross-cutting: it operates across N units (see <see cref="CostCenterUnit"/>).
/// Owns its budget as monthly <see cref="CostCenterUnitBudget"/> cells; the "annual budget" is the roll-up
/// (Σ of those cells) — there is no standalone budget field on the cost center anymore.</summary>
public class CostCenter : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Expenses up to this amount are approved automatically. 0 = nothing auto-approves.</summary>
    public decimal AutoApprovalLimit { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private CostCenter() { }

    public CostCenter(Guid companyId, string code, string name, decimal autoApprovalLimit = 0)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        AutoApprovalLimit = autoApprovalLimit;
    }

    public void Update(string code, string name, decimal autoApprovalLimit)
    {
        Code = code;
        Name = name;
        AutoApprovalLimit = autoApprovalLimit;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
