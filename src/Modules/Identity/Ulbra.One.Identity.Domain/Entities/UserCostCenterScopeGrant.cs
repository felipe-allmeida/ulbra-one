using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>Narrows a scoped user's access to a specific cost center within a granted company (AEL-883).
/// Third dimension of the intersection scope (company AND unit AND cost center), explicit lists only.
/// Cross-module ids are loose Guids; only the User link is a real FK. CompanyId is denormalized for the
/// per-company claim emission and the company↔cost-center validation.</summary>
public class UserCostCenterScopeGrant : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CostCenterId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private UserCostCenterScopeGrant() { }

    public UserCostCenterScopeGrant(Guid userId, Guid companyId, Guid costCenterId)
    {
        UserId = userId;
        CompanyId = companyId;
        CostCenterId = costCenterId;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
