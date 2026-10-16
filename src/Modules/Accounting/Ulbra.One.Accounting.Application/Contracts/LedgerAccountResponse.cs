using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>A node in the global accounting chart of accounts (plano de contas contábil).</summary>
[SwaggerSchema("A node of the chart of accounts (plano de contas). The chart is a tree: only leaf "
    + "accounts take postings, and the synthetic ones above exist to total them up.")]
public sealed record LedgerAccountResponse(
    LedgerAccountId Id,
    [property: SwaggerSchema("Hierarchical account code. Its structure is what places the account in the "
        + "tree, so it is not free-form.")]
    [property: SwaggerExample("3.1.2.04")]
    string Code,
    [property: SwaggerSchema("Short code used by the legacy ERP and by the accountants day to day. "
        + "Optional, and unrelated to the hierarchy.")]
    [property: SwaggerExample("41204")]
    string? ReducedCode,
    [property: SwaggerSchema("Natureza code carried over from the legacy chart. Kept for reconciliation "
        + "against legacy reports.")]
    [property: SwaggerExample("4")]
    int? Natureza,
    [property: SwaggerSchema("Account name as it appears in the reports.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string Name,
    [property: SwaggerSchema("Which side of the balance sheet or income statement the account belongs to.")]
    AccountGroup AccountGroup,
    [property: SwaggerSchema("The side that increases this account. It is what decides whether a balance "
        + "is shown as positive or negative in the reports.")]
    NormalBalance NormalBalance,
    [property: SwaggerSchema("Whether the account takes postings (analítica) or only totals its children "
        + "(sintética). Posting to a synthetic account is refused.")]
    LedgerAccountKind Kind,
    [property: SwaggerSchema("Parent account in the tree. Null only at the root of a group.")]
    LedgerAccountId? ParentId,
    [property: SwaggerSchema("Whether the account may be used. An inactive account keeps its history and "
        + "still appears in reports covering past periods, but is refused on new entries.")]
    bool IsActive,
    [property: SwaggerSchema("Which line of the DRE this account rolls into. Null means it does not "
        + "appear in the income statement — expected for balance-sheet accounts.")]
    DreLine? DreLine)
{
    public static LedgerAccountResponse From(LedgerAccount a) => new(
        new LedgerAccountId(a.Id),
        a.Code,
        a.ReducedCode,
        a.Natureza,
        a.Name,
        a.AccountGroup,
        a.NormalBalance,
        a.Kind,
        a.ParentId is { } p ? new LedgerAccountId(p) : null,
        a.IsActive,
        a.DreLine);
}
