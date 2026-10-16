namespace Ulbra.One.Common.Application.Authentication;

/// <summary>Encodes a user's per-unit data scope in the JWT — <see cref="ClaimType"/> claims in the
/// <c>{companyId:N}:{unitId:N}</c> format (AEL-883). Sibling of <see cref="ScopeClaim"/>. Explicit grants only
/// (no "empty = all") — a scoped user with no unit claims matches nothing (see <c>UserContext.CanAccessScope</c>).</summary>
public static class UnitScopeClaim
{
    /// <summary>Claim type carried in the JWT for each granted (company, unit) pair.</summary>
    public const string ClaimType = "uscope";

    /// <summary>Encodes a (company, unit) pair into the claim value.</summary>
    public static string Encode(Guid companyId, Guid unitId) => $"{companyId:N}:{unitId:N}";

    /// <summary>Parses the claim value back into a <see cref="UserUnitScope"/>.</summary>
    public static bool TryParse(string? value, out UserUnitScope scope)
    {
        scope = default;
        if (string.IsNullOrEmpty(value)) return false;

        var separator = value.IndexOf(':');
        if (separator < 0) return false;

        if (Guid.TryParseExact(value[..separator], "N", out var companyId)
            && Guid.TryParseExact(value[(separator + 1)..], "N", out var unitId))
        {
            scope = new UserUnitScope(companyId, unitId);
            return true;
        }

        return false;
    }
}
