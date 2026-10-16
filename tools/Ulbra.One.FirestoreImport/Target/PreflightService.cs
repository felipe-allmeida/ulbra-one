using Ulbra.One.FirestoreImport.Source;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Measures the import against the TARGET environment without writing. Distinguishes real
/// blockers (a unit code absent from the org — units are never auto-created) from naturezas that the
/// import will PROVISION on the fly under the unit's company. Run against dev or prod — the numbers come
/// from the target, never from the dev seed.</summary>
public sealed class PreflightService(FirestoreSource source, OrgResolver org)
{
    public const string AllSentinel = "all";

    public async Task<PreflightReport> RunAsync(CancellationToken ct)
    {
        var blocked = new Dictionary<string, int>();
        var toProvision = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var budgetTotal = 0;
        var budgetUnitResolved = 0;
        await foreach (var b in source.ReadBudgetsAsync(ct))
        {
            budgetTotal++;
            if (!org.TryResolveUnit(b.UnitCode, out var unit))
            {
                blocked[b.UnitCode ?? ""] = blocked.GetValueOrDefault(b.UnitCode ?? "") + 1;
                missingUnits.Add(b.UnitCode ?? "");
                continue;
            }

            budgetUnitResolved++;
            if (!org.TryResolveCostCenter(unit.CompanyId, b.NatureName, out _))
                toProvision.Add(b.NatureName ?? "");
        }

        var userTotal = 0;
        var userWithMissingUnit = 0;
        await foreach (var u in source.ReadUsersAsync(ct))
        {
            userTotal++;
            var missing = false;
            foreach (var code in u.Units.Where(x => !IsAll(x)))
                if (!org.TryResolveUnit(code, out _)) { missingUnits.Add(code); missing = true; }
            if (missing) userWithMissingUnit++;
        }

        var adjustmentTotal = 0;
        var adjustmentBlocked = 0;
        await foreach (var a in source.ReadAdjustmentsAsync(ct))
        {
            adjustmentTotal++;
            if (!org.TryResolveUnit(a.UnitCode, out var unit))
            {
                missingUnits.Add(a.UnitCode ?? "");
                adjustmentBlocked++;
                continue;
            }
            if (!org.TryResolveCostCenter(unit.CompanyId, a.NatureName, out _))
                toProvision.Add(a.NatureName ?? "");
        }

        var projectTotal = 0;
        var projectBlocked = 0;
        await foreach (var p in source.ReadProjectsAsync(ct))
        {
            projectTotal++;
            if (!org.TryResolveUnit(p.UnitCode, out _)) { missingUnits.Add(p.UnitCode ?? ""); projectBlocked++; }
        }

        return new PreflightReport
        {
            PrimaryCompanyFound = org.PrimaryCompanyId is not null,
            BudgetsTotal = budgetTotal,
            BudgetsUnitResolved = budgetUnitResolved,
            BudgetsBlockedMissingUnit = budgetTotal - budgetUnitResolved,
            BlockedBudgets = [.. blocked.Select(kv => new BlockedBudget(kv.Key, kv.Value)).OrderByDescending(b => b.Count)],
            NaturesToProvision = [.. toProvision.OrderBy(x => x)],
            UsersTotal = userTotal,
            UsersWithMissingUnit = userWithMissingUnit,
            MissingUnitCodes = [.. missingUnits.OrderBy(x => x)],
            AdjustmentsTotal = adjustmentTotal,
            AdjustmentsBlockedMissingUnit = adjustmentBlocked,
            ProjectsTotal = projectTotal,
            ProjectsBlockedMissingUnit = projectBlocked,
        };
    }

    private static bool IsAll(string token)
        => string.Equals(token, AllSentinel, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Budgets that cannot be imported because their unit code is absent from the target org.</summary>
public sealed record BlockedBudget(string UnitCode, int Count);

public sealed record PreflightReport
{
    public required bool PrimaryCompanyFound { get; init; }
    public required int BudgetsTotal { get; init; }
    public required int BudgetsUnitResolved { get; init; }
    public required int BudgetsBlockedMissingUnit { get; init; }
    public required IReadOnlyList<BlockedBudget> BlockedBudgets { get; init; }

    /// <summary>Distinct naturezas the import will create under the unit's company (informational).</summary>
    public required IReadOnlyList<string> NaturesToProvision { get; init; }

    public required int UsersTotal { get; init; }
    public required int UsersWithMissingUnit { get; init; }
    public required IReadOnlyList<string> MissingUnitCodes { get; init; }

    public required int AdjustmentsTotal { get; init; }
    public required int AdjustmentsBlockedMissingUnit { get; init; }
    public required int ProjectsTotal { get; init; }
    public required int ProjectsBlockedMissingUnit { get; init; }
}
