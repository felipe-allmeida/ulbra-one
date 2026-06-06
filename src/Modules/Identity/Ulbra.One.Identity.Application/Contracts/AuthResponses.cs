using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Identity.Application.Contracts;

/// <summary>User summary embedded in login responses. Matches the frontend <c>User</c> type.</summary>
public sealed record UserSummary(UserId Id, string Email, string Name, string Role);

/// <summary>Response for <c>/auth/google</c> and <c>/auth/dev-token</c>.</summary>
public sealed record LoginResponse(string Token, UserSummary User);

/// <summary>Response for <c>/auth/me</c>. Matches the frontend's inline me() type.</summary>
public sealed record MeResponse(UserId UserId, string? Name, string? Email, string? Role, bool IsAdmin);
