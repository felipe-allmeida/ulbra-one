namespace Ulbra.One.Common.Application.Authentication;

/// <summary>A user's per-cost-center data scope (AEL-883). Sibling of <see cref="UnitScopeClaim"/>.</summary>
public readonly record struct UserCostCenterScope(Guid CompanyId, Guid CostCenterId);

/// <summary>Encodes a user's per-cost-center data scope in the JWT — <see cref="ClaimType"/> claims in the
/// <c>{companyId:N}:{costCenterId:N}</c> format (AEL-883). Sibling of <see cref="UnitScopeClaim"/>. Explicit
/// grants only (no "empty = all") — a scoped user with no cost-center claims matches nothing.</summary>
public static class CostCenterScopeClaim
{
    public const string ClaimType = "cscope";

    public static string Encode(Guid companyId, Guid costCenterId) => $"{companyId:N}:{costCenterId:N}";

    public static bool TryParse(string? value, out UserCostCenterScope scope)
    {
        scope = default;
        if (string.IsNullOrEmpty(value)) return false;

        var separator = value.IndexOf(':');
        if (separator < 0) return false;

        if (Guid.TryParseExact(value[..separator], "N", out var companyId)
            && Guid.TryParseExact(value[(separator + 1)..], "N", out var costCenterId))
        {
            scope = new UserCostCenterScope(companyId, costCenterId);
            return true;
        }

        return false;
    }
}
