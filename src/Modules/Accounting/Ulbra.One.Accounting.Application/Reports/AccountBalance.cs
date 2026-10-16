using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Shared double-entry math for the report calculators.</summary>
public static class AccountBalance
{
    /// <summary>Signed contribution of a line to its account balance: +amount on the account's normal side,
    /// −amount on the opposite side (debit−credit for debit-normal accounts, credit−debit for credit-normal).
    /// Shared by the Balancete, Livro Razão and Balanço so their balances never diverge.</summary>
    public static decimal Signed(NormalBalance normal, EntrySide side, decimal amount)
    {
        var onNormalSide = (normal == NormalBalance.Debit) == (side == EntrySide.Debit);
        return onNormalSide ? amount : -amount;
    }
}
