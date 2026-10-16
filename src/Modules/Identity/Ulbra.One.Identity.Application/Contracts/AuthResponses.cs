using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>User summary embedded in login responses. Matches the frontend <c>User</c> type.</summary>
[SwaggerSchema("Who just signed in — the minimum the client needs to render the header before it calls "
    + "/auth/me for the full profile.")]
public sealed record UserSummary(
    UserId Id,
    [property: SwaggerSchema("Google account the user signed in with. It is the identity key: the account "
        + "is matched by e-mail, so changing it in Google creates a different user here.")]
    [property: SwaggerExample("redacted-12@example.invalid")]
    string Email,
    [property: SwaggerSchema("Display name, taken from the Google profile.")]
    [property: SwaggerExample("Maria Silva")]
    string Name,
    [property: SwaggerSchema("Role name. It carries the permissions, but the effective list is on "
        + "/auth/me — do not gate the UI on the name.")]
    [property: SwaggerExample("Controladoria")]
    string Role);

/// <summary>Response for <c>/auth/google</c> and <c>/auth/dev-token</c>.</summary>
[SwaggerSchema("A successful sign-in: the bearer token plus who it belongs to.")]
public sealed record LoginResponse(
    [property: SwaggerSchema("JWT to send as `Authorization: Bearer <token>`. It carries the role, the "
        + "permissions and the requester's scope, so anything that changes those only takes effect on the "
        + "next sign-in.")]
    string Token,
    [property: SwaggerSchema("The signed-in user.")]
    UserSummary User);

/// <summary>Response for <c>/auth/me</c>. Matches the frontend's inline me() type.</summary>
/// <param name="Permissions">Permissões efetivas do usuário (vazio para Admin — ele faz bypass; o front usa <c>IsAdmin</c>).</param>
/// <param name="CompanyIds">Companies the user can access (scope grants). Empty for Admin — it bypasses scope. See AEL-764.</param>
/// <param name="CanApproveBudgetAdjustment">Per-user grant to approve/reverse budget adjustments (AEL-883); the front hides the
/// approve control unless this is true (Admin is always true). Enforcement is live server-side, so this is only a UI hint.</param>
[SwaggerSchema("The caller's own profile — identity, role and everything the UI needs to decide what to "
    + "show. This is what the SPA gates its menu and controls on.")]
public sealed record MeResponse(
    UserId UserId,
    [property: SwaggerSchema("Display name from the Google profile.")]
    [property: SwaggerExample("Maria Silva")]
    string? Name,
    [property: SwaggerSchema("Google account the user signs in with.")]
    [property: SwaggerExample("redacted-12@example.invalid")]
    string? Email,
    [property: SwaggerSchema("Role name.")]
    [property: SwaggerExample("Controladoria")]
    string? Role,
    [property: SwaggerSchema("Whether the user is an Admin. Admin bypasses every permission and scope "
        + "check, which is why Permissions and CompanyIds come back empty for one — read this flag first, "
        + "or an Admin looks like a user with no access at all.")]
    bool IsAdmin,
    [property: SwaggerSchema("Effective permissions, in `resource:action` form. Empty for an Admin (see "
        + "IsAdmin) — not because they have none, but because they bypass the check.")]
    IReadOnlyCollection<string> Permissions,
    [property: SwaggerSchema("Companies the user may access. Empty for an Admin, who bypasses scope. For "
        + "a scoped role an empty list means no access, not full access.")]
    IReadOnlyCollection<CompanyId> CompanyIds,
    [property: SwaggerSchema("Whether the user may approve budget adjustments. A UI hint only — the "
        + "server checks this again at approval time, and reads it live rather than from the token.")]
    bool CanApproveBudgetAdjustment);
