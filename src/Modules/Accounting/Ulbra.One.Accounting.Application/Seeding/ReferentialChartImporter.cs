using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Builds the global ledger chart from RFB referencial rows. Group comes from
/// <c>natureza</c> (resolving PL nested under "2"); kind from <c>tipo</c>; parents are wired
/// from "conta superior" but only when the parent shares the same group and is synthetic —
/// otherwise the node becomes a group root, preserving <see cref="LedgerAccount.Reparent"/>'s
/// same-group invariant without touching the domain.</summary>
public static class ReferentialChartImporter
{
    public static IReadOnlyList<LedgerAccount> Build(IReadOnlyList<ReferentialAccountRow> rows)
    {
        var byCode = new Dictionary<string, LedgerAccount>(rows.Count);
        var ordered = new List<LedgerAccount>(rows.Count);
        foreach (var row in rows)
        {
            var group = GroupOf(row);
            var kind = row.Tipo == "S" ? LedgerAccountKind.Synthetic : LedgerAccountKind.Analytical;
            var account = new LedgerAccount(row.Code, row.Name, group, kind);
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

    private static AccountGroup GroupOf(ReferentialAccountRow row) => row.Natureza switch
    {
        "1" => AccountGroup.Asset,
        "2" => AccountGroup.Liability,
        "3" => AccountGroup.Equity,
        "4" => ResultAccountClassifier.Classify(row.Code, row.Name),
        _ => throw new InvalidOperationException($"Unknown natureza '{row.Natureza}' for account {row.Code}")
    };
}
