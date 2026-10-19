using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>User row for the access-admin screen: identity + role + Ativo + pending-first-login + the scope (companies and, within
/// them, units and cost centers). Only the scoped roles (Requester/Approver) use the scope; other roles and
/// Admin see everything (empty scope collections). Explicit lists — no "empty = all" (AEL-883).</summary>
[SwaggerSchema("A user as the access-admin screen sees them: identity, role, whether they can sign in, "
    + "and the full scope they act within.")]
public sealed record UserAdminResponse(
    UserId Id,
    [property: SwaggerSchema("Google account the user signs in with.")]
    [property: SwaggerExample("redacted-13@example.invalid")]
    string Email,
    [property: SwaggerSchema("Display name from the Google profile.")]
    [property: SwaggerExample("Maria Silva")]
    string Name,
    [property: SwaggerSchema("Profile photo from the Google account, refreshed at every sign-in. Null for a "
        + "user who has never signed in or whose account has no photo — fall back to initials. The URL is "
        + "Google's own and rotates when the photo changes, so treat a failed load as \"no photo\" too.")]
    [property: SwaggerExample("https://lh3.googleusercontent.com/a/ACg8ocK")]
    string? PictureUrl,
    [property: SwaggerSchema("Role name. Changing it takes effect on the user's next sign-in, because the "
        + "permissions travel in the token.")]
    [property: SwaggerExample("Controladoria")]
    string Role,
    [property: SwaggerSchema("Whether the user is an Admin, and so bypasses every permission and scope "
        + "check. The scope collections below are empty for one.")]
    bool IsAdmin,
    [property: SwaggerSchema("Whether the user may sign in. Deactivating blocks the next sign-in and "
        + "leaves an already-issued token valid until it expires.")]
    bool Active,
    [property: SwaggerSchema("Whether the user was pre-registered and has never signed in. The role and "
        + "scope below are what their first Google login will carry.")]
    bool PendingFirstLogin,
    [property: SwaggerSchema("Whether the user may approve budget adjustments. Read live, so it applies "
        + "immediately rather than on next sign-in.")]
    bool CanApproveBudgetAdjustment,
    [property: SwaggerSchema("Companies granted to the user. Explicit list: empty means no access, not "
        + "full access — except for an Admin, who bypasses scope entirely.")]
    IReadOnlyCollection<CompanyId> CompanyIds,
    [property: SwaggerSchema("Units granted, each paired with its company. Only the scoped roles "
        + "(Requester, Approver) are constrained by it; for the others it is stored and ignored.")]
    IReadOnlyCollection<UnitScopeAssignment> Units,
    [property: SwaggerSchema("Cost centers granted, each paired with its company. Same rule as Units.")]
    IReadOnlyCollection<CostCenterScopeAssignment> CostCenters);

/// <summary>Pre-registers a user by e-mail (convite): the row is created before the person ever signs in, so
/// the first Google login already lands on the intended role and scope.</summary>
public sealed record CreateUserRequest(
    [property: SwaggerSchema("The Google account e-mail that will be allowed to sign in. Stored lowercase; "
        + "must be unique. Registering is itself the authorization — the e-mail signs in even if its domain "
        + "is not on the AllowedDomains list.")]
    [property: SwaggerExample("fulano@aelbra.com.br")]
    string Email,
    [property: SwaggerSchema("Role name, which must match one of the seeded roles returned by "
        + "GET /api/roles. An unknown name returns 400.")]
    [property: SwaggerExample("Controllership")]
    string Role,
    [property: SwaggerSchema("Display name shown until the first sign-in, when Google's own name replaces it.")]
    string? Name = null,
    [property: SwaggerSchema("Companies the user may access. Same rules as PUT /users/{id}/scope: explicit "
        + "list, and every scoped unit / cost center must belong to a granted company.")]
    IReadOnlyCollection<CompanyId>? CompanyIds = null,
    [property: SwaggerSchema("Units the user may act in, each paired with its company.")]
    IReadOnlyCollection<UnitScopeAssignment>? Units = null,
    [property: SwaggerSchema("Cost centers the user may act in, each paired with its company.")]
    IReadOnlyCollection<CostCenterScopeAssignment>? CostCenters = null,
    [property: SwaggerSchema("Approval authority ceiling (alçada) for adequações. Null means unlimited; a "
        + "negative value returns 400.")]
    decimal? ApprovalLimit = null,
    [property: SwaggerSchema("Whether this user may approve and reverse budget adjustments. Only meaningful "
        + "for Controllership users, who hold budget-adjustments:approve.")]
    bool CanApproveBudgetAdjustment = false);

/// <summary>A granted (company, unit) pair — the wire shape of a unit scope grant (AEL-883).</summary>
[SwaggerSchema("A granted (company, unit) pair. The unit is keyed on its own id, so the same unit cannot "
    + "appear under two companies.")]
public sealed record UnitScopeAssignment(CompanyId CompanyId, UnitId UnitId);

/// <summary>A granted (company, cost center) pair — the wire shape of a cost-center scope grant (AEL-883).</summary>
[SwaggerSchema("A granted (company, cost center) pair. Same keying rule as the unit pair.")]
public sealed record CostCenterScopeAssignment(CompanyId CompanyId, CostCenterId CostCenterId);

/// <summary>Replaces the full scope a user can access (grant/revoke by diff): companies plus the specific
/// units and cost centers within them. Explicit lists (no "empty = all") — the UI's "Selecionar todos"
/// stores the current snapshot. Only meaningful for scoped roles (Requester/Approver). AEL-883.</summary>
public sealed record SetUserScopeRequest(
    [property: SwaggerSchema("Companies the user may access. Explicit list — an empty one grants "
        + "nothing rather than everything, and a company left out is revoked.")]
    IReadOnlyCollection<CompanyId> CompanyIds,
    [property: SwaggerSchema("Units the user may act in, each paired with its company. That company must "
        + "also be in CompanyIds, otherwise the whole call is rejected with 400.")]
    IReadOnlyCollection<UnitScopeAssignment>? Units = null,
    [property: SwaggerSchema("Cost centers the user may act in, each paired with its company. Same rule "
        + "as Units: the company must be granted too.")]
    IReadOnlyCollection<CostCenterScopeAssignment>? CostCenters = null);

/// <summary>Sets a user's role by name (must be an existing seeded role). Effective on next login. AEL-883.</summary>
public sealed record SetUserRoleRequest(
    [property: SwaggerSchema("Role name, which must match one of the seeded roles returned by "
        + "GET /api/roles. An unknown name returns 400.")]
    [property: SwaggerExample("Controladoria")]
    string Role);

/// <summary>Activates/deactivates a user. Inactive users are denied at login (token issuance). AEL-883.</summary>
public sealed record SetUserActiveRequest(
    [property: SwaggerSchema("Whether the user may sign in. Deactivating blocks the NEXT sign-in; it does "
        + "not revoke a token already issued, which stays valid until it expires.")]
    bool Active);

/// <summary>Grants/revokes a user's authority to approve budget adjustments (adequações). Effective immediately
/// (read live). Only meaningful for Controladoria users, who hold budget-adjustments:approve. AEL-883.</summary>
public sealed record SetBudgetAdjustmentApproverRequest(
    [property: SwaggerSchema("Whether this user may approve and reverse budget adjustments. Read live at "
        + "approval time, so revoking it takes effect immediately — no sign-in needed.")]
    bool CanApprove);
