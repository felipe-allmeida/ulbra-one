using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Splits a result account (natureza "4") into Revenue or Expense.
/// The RFB descriptions are explicit ("RECEITA…"), so a name match decides; a small
/// override table by code covers the rare ambiguous cases. Synthetic calculation nodes
/// (e.g. "RESULTADO LÍQUIDO") have no journal lines, so their group is only cosmetic and
/// falls back to Expense.</summary>
public static class ResultAccountClassifier
{
    private static readonly IReadOnlyDictionary<string, AccountGroup> Overrides =
        new Dictionary<string, AccountGroup>();

    public static AccountGroup Classify(string code, string name)
    {
        if (Overrides.TryGetValue(code, out var group))
            return group;
        return name.Contains("RECEITA", StringComparison.OrdinalIgnoreCase)
            ? AccountGroup.Revenue
            : AccountGroup.Expense;
    }
}
