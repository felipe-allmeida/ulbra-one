using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>Narrows a user's data access to a specific unit within a granted company (AEL-883): explicit
/// grants only (no "empty = all") — a scoped user needs the unit granted, an empty unit scope blocks. Emitted
/// as <c>uscope</c> claims at login. Cross-module ids are loose Guids; only the User link is a real FK.
/// CompanyId is stored for per-company claim emission.</summary>
public class UserUnitScopeGrant : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private UserUnitScopeGrant() { }

    public UserUnitScopeGrant(Guid userId, Guid companyId, Guid unitId)
    {
        UserId = userId;
        CompanyId = companyId;
        UnitId = unitId;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
