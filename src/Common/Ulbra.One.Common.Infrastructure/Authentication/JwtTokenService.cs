using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Common.Infrastructure.Authentication;

public class JwtTokenService(IConfiguration configuration, TimeProvider clock) : ITokenService
{
    public const string Issuer = "ulbra-one";
    public const string Audience = "ulbra-one-api";

    private readonly string _secret = configuration["Authentication:JwtSecret"]
        ?? throw new InvalidOperationException("Authentication:JwtSecret is required");

    public string GenerateToken(string userId, string role, string? userName = null, string? userEmail = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role),
        };

        if (userName is not null) claims.Add(new Claim(ClaimTypes.Name, userName));
        if (userEmail is not null) claims.Add(new Claim(ClaimTypes.Email, userEmail));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: clock.GetUtcNow().UtcDateTime.AddDays(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
