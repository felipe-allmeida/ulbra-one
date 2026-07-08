using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>A node in the global accounting chart of accounts (plano de contas contábil).</summary>
public sealed record LedgerAccountResponse(
    LedgerAccountId Id,
    string Code,
    string Name,
    AccountGroup AccountGroup,
    NormalBalance NormalBalance,
    LedgerAccountKind Kind,
    LedgerAccountId? ParentId,
    bool IsActive)
{
    public static LedgerAccountResponse From(LedgerAccount a) => new(
        new LedgerAccountId(a.Id),
        a.Code,
        a.Name,
        a.AccountGroup,
        a.NormalBalance,
        a.Kind,
        a.ParentId is { } p ? new LedgerAccountId(p) : null,
        a.IsActive);
}
