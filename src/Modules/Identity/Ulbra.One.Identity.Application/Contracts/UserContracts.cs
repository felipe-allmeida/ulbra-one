using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>Sets a user's approval authority (alçada). <c>ApprovalLimit = null</c> means unlimited.</summary>
public sealed record SetApprovalAuthorityRequest(
    [property: SwaggerSchema("Highest value this user may approve, in BRL (alçada). Null means "
        + "unlimited. It is a hard cap with no override: a budget adjustment above it is refused.")]
    [property: SwaggerExample("50000.00")]
    decimal? ApprovalLimit,
    [property: SwaggerSchema("Autonomy flag. Stored and returned on the user, and carried in the approval "
        + "authority profile, but no approval flow reads it today — only ApprovalLimit and the "
        + "budget-adjustment approver grant are enforced. Setting it changes nothing on its own.")]
    bool HasApprovalAutonomy);

/// <summary>A user's approval authority after an update.</summary>
[SwaggerSchema("A user's approval authority (alçada) after an update.")]
public sealed record UserApprovalAuthorityResponse(
    UserId Id,
    [property: SwaggerSchema("Display name.")]
    [property: SwaggerExample("Maria Silva")]
    string Name,
    [property: SwaggerSchema("Google account the user signs in with.")]
    [property: SwaggerExample("redacted-12@example.invalid")]
    string Email,
    [property: SwaggerSchema("Highest value this user may approve, in BRL. Null means unlimited. It is a "
        + "hard cap with no override.")]
    [property: SwaggerExample("50000.00")]
    decimal? ApprovalLimit,
    [property: SwaggerSchema("Autonomy flag. Stored and echoed here, but no approval flow reads it today "
        + "— only ApprovalLimit and the budget-adjustment approver grant are enforced. Do not gate "
        + "anything on it.")]
    bool HasApprovalAutonomy);
