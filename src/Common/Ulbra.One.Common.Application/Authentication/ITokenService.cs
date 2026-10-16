namespace Ulbra.One.Common.Application.Authentication;

/// <summary>Issues signed authentication tokens. The implementation (JWT) lives in the infrastructure layer.</summary>
public interface ITokenService
{
    /// <param name="permissions">Role permissions, issued as <see cref="Permissions.ClaimType"/> claims.</param>
    /// <param name="scopes">Granted scopes (companies), issued as <see cref="ScopeClaim.ClaimType"/> claims.</param>
    /// <param name="unitScopes">Unit scopes (company:unit), issued as <see cref="UnitScopeClaim.ClaimType"/> claims (AEL-883).</param>
    /// <param name="costCenterScopes">Cost-center scopes (company:cost center), issued as <see cref="CostCenterScopeClaim.ClaimType"/> claims (AEL-883).</param>
    string GenerateToken(string userId, string role, string? userName = null, string? userEmail = null, IEnumerable<string>? permissions = null, IEnumerable<string>? scopes = null, IEnumerable<string>? unitScopes = null, IEnumerable<string>? costCenterScopes = null);
}
