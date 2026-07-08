using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class LedgerAccountTests
{
    private static LedgerAccount Synthetic(AccountGroup group = AccountGroup.Asset) =>
        new("1", "Ativo", group, LedgerAccountKind.Synthetic);

    private static LedgerAccount Analytical(AccountGroup group = AccountGroup.Asset) =>
        new("1.1", "Caixa", group, LedgerAccountKind.Analytical);

    [Fact]
    public void Constructor_derives_normal_balance_from_group()
    {
        Assert.Equal(NormalBalance.Debit, Analytical(AccountGroup.Asset).NormalBalance);
        Assert.Equal(NormalBalance.Debit, Analytical(AccountGroup.Expense).NormalBalance);
        Assert.Equal(NormalBalance.Credit, Analytical(AccountGroup.Liability).NormalBalance);
        Assert.Equal(NormalBalance.Credit, Analytical(AccountGroup.Equity).NormalBalance);
        Assert.Equal(NormalBalance.Credit, Analytical(AccountGroup.Revenue).NormalBalance);
    }

    [Fact]
    public void Constructor_honours_explicit_normal_balance()
    {
        var acct = new LedgerAccount("1", "X", AccountGroup.Asset, LedgerAccountKind.Analytical,
            normalBalance: NormalBalance.Credit);
        Assert.Equal(NormalBalance.Credit, acct.NormalBalance);
    }

    [Fact]
    public void Constructor_trims_and_requires_code_and_name()
    {
        var acct = new LedgerAccount("  1.1  ", "  Caixa  ", AccountGroup.Asset, LedgerAccountKind.Analytical);
        Assert.Equal("1.1", acct.Code);
        Assert.Equal("Caixa", acct.Name);
        Assert.Throws<ArgumentException>(() => new LedgerAccount("  ", "Caixa", AccountGroup.Asset, LedgerAccountKind.Analytical));
        Assert.Throws<ArgumentNullException>(() => new LedgerAccount(null!, "Caixa", AccountGroup.Asset, LedgerAccountKind.Analytical));
    }

    [Fact]
    public void CanReceiveEntry_true_only_for_active_analytical_not_deleted()
    {
        Assert.True(Analytical().CanReceiveEntry);
        Assert.False(Synthetic().CanReceiveEntry);

        var inactive = Analytical();
        inactive.Deactivate();
        Assert.False(inactive.CanReceiveEntry);

        var deleted = Analytical();
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.False(deleted.CanReceiveEntry);
    }

    [Fact]
    public void Reparent_to_synthetic_same_group_sets_parent()
    {
        var parent = Synthetic(AccountGroup.Asset);
        var child = Analytical(AccountGroup.Asset);
        child.Reparent(parent);
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public void Reparent_null_clears_parent()
    {
        var child = Analytical();
        child.Reparent(Synthetic());
        child.Reparent(null);
        Assert.Null(child.ParentId);
    }

    [Fact]
    public void Reparent_rejects_analytical_parent()
    {
        var parent = Analytical(AccountGroup.Asset);
        var child = Analytical(AccountGroup.Asset);
        Assert.Throws<InvalidOperationException>(() => child.Reparent(parent));
    }

    [Fact]
    public void Reparent_rejects_different_group()
    {
        var parent = Synthetic(AccountGroup.Liability);
        var child = Analytical(AccountGroup.Asset);
        Assert.Throws<InvalidOperationException>(() => child.Reparent(parent));
    }

    [Fact]
    public void Reparent_rejects_self()
    {
        var acct = Synthetic();
        Assert.Throws<InvalidOperationException>(() => acct.Reparent(acct));
    }

    [Fact]
    public void Rename_and_Update_change_fields()
    {
        var acct = Analytical();
        acct.Rename("Bancos");
        Assert.Equal("Bancos", acct.Name);

        acct.Update("Caixa Geral", AccountGroup.Asset, LedgerAccountKind.Analytical, NormalBalance.Debit);
        Assert.Equal("Caixa Geral", acct.Name);
    }

    [Fact]
    public void Activate_Deactivate_toggle_IsActive()
    {
        var acct = Analytical();
        acct.Deactivate();
        Assert.False(acct.IsActive);
        acct.Activate();
        Assert.True(acct.IsActive);
    }
}
