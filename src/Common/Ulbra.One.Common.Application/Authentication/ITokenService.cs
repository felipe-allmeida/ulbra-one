namespace Ulbra.One.Common.Application.Authentication;

/// <summary>Issues signed authentication tokens. The implementation (JWT) lives in the infrastructure layer.</summary>
public interface ITokenService
{
    string GenerateToken(string userId, string role, string? userName = null, string? userEmail = null);
}
