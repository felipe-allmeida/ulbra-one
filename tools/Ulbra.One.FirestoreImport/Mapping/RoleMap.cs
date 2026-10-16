namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Maps legacy PT-BR role labels to the app's canonical role strings
/// (src/Modules/Identity/Ulbra.One.Identity.Domain/Roles.cs). Unknown labels fall back to "User"
/// and report <c>matched = false</c> so the runner can warn instead of silently downgrading access.</summary>
public static class RoleMap
{
    public const string Fallback = "User";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Administrador"] = "Admin",
        ["Presidência"] = "Presidency",
        ["Presidencia"] = "Presidency",
        ["Aprovador"] = "Approver",
        ["Controle"] = "Controllership",
        ["Solicitante"] = "Requester",
        ["Financeiro"] = "Finance",
        ["Contabilidade"] = "Accounting",
        ["Projeto"] = "User", // no exact One equivalent — default, pending confirmation
    };

    // Higher wins when a user carries several legacy roles (role + roles[]). One stores a single role.
    private static readonly Dictionary<string, int> Priority = new()
    {
        ["Admin"] = 100,
        ["Presidency"] = 90,
        ["Finance"] = 80,
        ["Controllership"] = 70,
        ["Accounting"] = 60,
        ["Approver"] = 50,
        ["Tax"] = 40,
        ["Requester"] = 20,
        ["User"] = 10,
    };

    public static string Resolve(string? legacyRole, out bool matched)
    {
        var key = (legacyRole ?? string.Empty).Trim();
        matched = Map.TryGetValue(key, out var role);
        return matched ? role! : Fallback;
    }

    /// <summary>Chooses the highest-privilege One role among a user's primary role and any extra
    /// <c>roles[]</c>. <paramref name="anyMatched"/> is false when none matched a known label (all fell
    /// back to "User"), so the runner can warn.</summary>
    public static string ResolveBest(string? primary, IEnumerable<string> multi, out bool anyMatched)
    {
        anyMatched = false;
        var best = Fallback;
        var bestRank = -1;

        foreach (var candidate in Prepend(primary, multi))
        {
            var role = Resolve(candidate, out var matched);
            if (matched) anyMatched = true;
            var rank = Priority.GetValueOrDefault(role, 0);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = role;
            }
        }

        return best;
    }

    private static IEnumerable<string> Prepend(string? primary, IEnumerable<string> multi)
    {
        if (!string.IsNullOrWhiteSpace(primary)) yield return primary;
        foreach (var m in multi) yield return m;
    }
}
