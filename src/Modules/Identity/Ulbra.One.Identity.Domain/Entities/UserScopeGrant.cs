using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>
/// Grants a user access to a company's resources (legal entity). It is the basis of per-resource
/// access control (AEL-764): the grants are denormalized into <c>scope</c> claims at login and
/// checked by <c>UserContext.CanAccess</c>. The company is referenced by a loose Guid (cross-schema,
/// no FK); the link to the user is a real FK (same identity schema).
/// </summary>
public class UserScopeGrant : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private UserScopeGrant() { }

    public UserScopeGrant(Guid userId, Guid companyId)
    {
        UserId = userId;
        CompanyId = companyId;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
