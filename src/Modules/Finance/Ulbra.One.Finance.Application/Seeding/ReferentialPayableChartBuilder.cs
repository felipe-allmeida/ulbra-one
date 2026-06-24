using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Seeding;

/// <summary>Builds the Accounting payable-classification chart (<see cref="ChartType.Accounting"/>)
/// from referencial rows already filtered to the expense branch. Analytical accounts get a Debit
/// nature; parents are wired within the supplied set (synthetic parents only).</summary>
public static class ReferentialPayableChartBuilder
{
    public static IReadOnlyList<ChartAccount> BuildAccounting(IReadOnlyList<ReferentialPayableRow> rows)
    {
        var byCode = new Dictionary<string, ChartAccount>(rows.Count);
        var ordered = new List<ChartAccount>(rows.Count);
        foreach (var row in rows)
        {
            var kind = row.IsSynthetic ? AccountKind.Synthetic : AccountKind.Analytical;
            AccountNature? nature = kind == AccountKind.Analytical ? AccountNature.Debit : null;
            var account = new ChartAccount(ChartType.Accounting, row.Code, row.Name, kind, nature: nature);
            byCode[row.Code] = account;
            ordered.Add(account);
        }

        foreach (var row in rows)
        {
            if (row.Parent is null) continue;
            if (byCode.TryGetValue(row.Parent, out var parent) && parent.Kind == AccountKind.Synthetic)
                byCode[row.Code].Reparent(parent);
        }

        return ordered;
    }
}
