using System.Text.Json;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Builds the single global ledger chart from AELBRA's "Modelo de Planos" (Senior export).
/// The chart is the one canonical plano de contas contábil — no other template exists.
/// <list type="bullet">
/// <item>Group comes from the code's root digit (1→Asset, 2→Liability, 3→Revenue, 4/5→Expense),
/// with the "2.6" Patrimônio Líquido subtree mapped to Equity.</item>
/// <item>Kind from <c>tipo</c> (S/A); normal balance from the explicit <c>normalBalance</c> (D/C),
/// so contra-accounts keep the correct side; the <c>natureza</c> (1-5) classification is stored verbatim.</item>
/// <item>Parents are wired from the code hierarchy, but only when the parent shares the same group
/// and is synthetic — otherwise the node becomes a group root (this detaches "2.6" as an Equity root),
/// preserving <see cref="LedgerAccount.Reparent"/>'s same-group invariant.</item>
/// </list></summary>
public static class AelbraChart
{
    private const string ResourceName = "aelbra-plano-contabil-sa.json";
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<LedgerAccount> Build()
    {
        var rows = Load();
        var byCode = new Dictionary<string, LedgerAccount>(rows.Count);
        var ordered = new List<LedgerAccount>(rows.Count);
        foreach (var row in rows)
        {
            var account = new LedgerAccount(
                row.Code, row.Name, GroupOf(row.Code), KindOf(row.Tipo),
                parentId: null, NormalBalanceOf(row.NormalBalance), row.ReducedCode, row.Natureza);
            byCode[row.Code] = account;
            ordered.Add(account);
        }

        foreach (var row in rows)
        {
            if (row.Parent is null) continue;
            var child = byCode[row.Code];
            if (byCode.TryGetValue(row.Parent, out var parent)
                && parent.Kind == LedgerAccountKind.Synthetic
                && parent.AccountGroup == child.AccountGroup)
            {
                child.Reparent(parent);
            }
        }

        return ordered;
    }

    private static IReadOnlyList<AelbraAccountRow> Load()
    {
        var assembly = typeof(AelbraChart).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {ResourceName}");
        return JsonSerializer.Deserialize<List<AelbraAccountRow>>(stream, Options)
            ?? throw new InvalidOperationException($"Failed to parse resource: {ResourceName}");
    }

    private static AccountGroup GroupOf(string code) => code[0] switch
    {
        '1' => AccountGroup.Asset,
        '2' => code == "2.6" || code.StartsWith("2.6.", StringComparison.Ordinal)
            ? AccountGroup.Equity
            : AccountGroup.Liability,
        '3' => AccountGroup.Revenue,
        '4' => AccountGroup.Expense,
        '5' => AccountGroup.Expense,
        _ => throw new InvalidOperationException($"Unknown root for code '{code}'.")
    };

    private static LedgerAccountKind KindOf(string tipo) => tipo switch
    {
        "S" => LedgerAccountKind.Synthetic,
        "A" => LedgerAccountKind.Analytical,
        _ => throw new InvalidOperationException($"Unknown tipo '{tipo}'.")
    };

    private static NormalBalance NormalBalanceOf(string normalBalance) => normalBalance switch
    {
        "D" => NormalBalance.Debit,
        "C" => NormalBalance.Credit,
        _ => throw new InvalidOperationException($"Unknown normal balance '{normalBalance}'.")
    };
}
