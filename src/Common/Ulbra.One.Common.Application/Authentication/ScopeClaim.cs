namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Encodes the user's access scope for transport in the JWT — claims of type
/// <see cref="ClaimType"/> in the <c>{companyId:N}</c> format. Denormalized at login (sibling of
/// the <see cref="Permissions"/> pattern), it keeps <c>JwtTokenService</c> "dumb" (it just
/// concatenates strings). Single encode/parse point to ease extending by unit in the future.
/// </summary>
public static class ScopeClaim
{
    /// <summary>Claim type carried in the JWT for each granted scope (company).</summary>
    public const string ClaimType = "scope";

    /// <summary>Encodes a company's scope into the claim value.</summary>
    public static string Encode(Guid companyId) => companyId.ToString("N");

    /// <summary>Parses the claim value back into a <see cref="UserScope"/>.</summary>
    public static bool TryParse(string? value, out UserScope scope)
    {
        if (Guid.TryParseExact(value, "N", out var companyId))
        {
            scope = new UserScope(companyId);
            return true;
        }

        scope = default;
        return false;
    }
}
