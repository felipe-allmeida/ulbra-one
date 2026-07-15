namespace Ulbra.One.Common.Application.Authentication;

/// <summary>Issues signed authentication tokens. The implementation (JWT) lives in the infrastructure layer.</summary>
public interface ITokenService
{
    /// <param name="permissions">Permissões do cargo, emitidas como claims <see cref="Permissions.ClaimType"/>.</param>
    string GenerateToken(string userId, string role, string? userName = null, string? userEmail = null, IEnumerable<string>? permissions = null);
}
