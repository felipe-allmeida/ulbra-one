using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class ChartAccountTests
{
    private static ChartAccount NewSynthetic(ChartType chartType = ChartType.Accounting, Guid? parentId = null) =>
        new(chartType, "1", "Receitas", AccountKind.Synthetic, parentId);

    private static ChartAccount NewAnalytical(ChartType chartType = ChartType.Accounting, Guid? parentId = null) =>
        new(chartType, "1.1", "Vendas", AccountKind.Analytical, parentId);

    // --- Constructor ---

    [Fact]
    public void Constructor_sets_all_properties()
    {
        var parentId = Guid.NewGuid();
        var account = new ChartAccount(ChartType.Financial, " 2.1 ", " Despesas ", AccountKind.Analytical,
            parentId, AccountNature.Debit);

        Assert.Equal(ChartType.Financial, account.ChartType);
        Assert.Equal("2.1", account.Code);
        Assert.Equal("Despesas", account.Name);
        Assert.Equal(AccountKind.Analytical, account.Kind);
        Assert.Equal(parentId, account.ParentId);
        Assert.Equal(AccountNature.Debit, account.Nature);
    }

    [Fact]
    public void Constructor_IsActive_true_by_default()
    {
        var account = NewAnalytical();
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Constructor_trims_code_and_name()
    {
        var account = new ChartAccount(ChartType.Accounting, "  A1  ", "  My Account  ", AccountKind.Synthetic);
        Assert.Equal("A1", account.Code);
        Assert.Equal("My Account", account.Name);
    }

    [Fact]
    public void Constructor_throws_on_null_code()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ChartAccount(ChartType.Accounting, null!, "Name", AccountKind.Synthetic));
    }

    [Fact]
    public void Constructor_throws_on_empty_code()
    {
        Assert.Throws<ArgumentException>(() =>
            new ChartAccount(ChartType.Accounting, "  ", "Name", AccountKind.Synthetic));
    }

    [Fact]
    public void Constructor_throws_on_null_name()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ChartAccount(ChartType.Accounting, "1", null!, AccountKind.Synthetic));
    }

    [Fact]
    public void Constructor_throws_on_empty_name()
    {
        Assert.Throws<ArgumentException>(() =>
            new ChartAccount(ChartType.Accounting, "1", "   ", AccountKind.Synthetic));
    }

    // --- CanReceivePayable ---

    [Fact]
    public void CanReceivePayable_true_for_active_analytical_account()
    {
        var account = NewAnalytical();
        Assert.True(account.CanReceivePayable);
    }

    [Fact]
    public void CanReceivePayable_false_for_synthetic_account()
    {
        var account = NewSynthetic();
        Assert.False(account.CanReceivePayable);
    }

    [Fact]
    public void CanReceivePayable_false_when_inactive()
    {
        var account = NewAnalytical();
        account.Deactivate();
        Assert.False(account.CanReceivePayable);
    }

    [Fact]
    public void CanReceivePayable_false_when_soft_deleted()
    {
        var account = NewAnalytical();
        account.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.False(account.CanReceivePayable);
    }

    // --- Reparent ---

    [Fact]
    public void Reparent_to_synthetic_same_type_sets_parent_id()
    {
        var parent = NewSynthetic(ChartType.Accounting);
        var child = NewAnalytical(ChartType.Accounting);

        child.Reparent(parent);

        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public void Reparent_null_clears_parent_id()
    {
        var parent = NewSynthetic();
        var child = NewAnalytical();
        child.Reparent(parent);

        child.Reparent(null);

        Assert.Null(child.ParentId);
    }

    [Fact]
    public void Reparent_to_different_chart_type_throws()
    {
        var parent = NewSynthetic(ChartType.Financial);
        var child = NewAnalytical(ChartType.Accounting);

        Assert.Throws<InvalidOperationException>(() => child.Reparent(parent));
    }

    [Fact]
    public void Reparent_to_analytical_parent_throws()
    {
        var analyticalParent = NewAnalytical(ChartType.Accounting);
        var child = NewAnalytical(ChartType.Accounting);

        Assert.Throws<InvalidOperationException>(() => child.Reparent(analyticalParent));
    }

    [Fact]
    public void Reparent_self_throws()
    {
        var account = NewSynthetic(ChartType.Accounting);

        Assert.Throws<InvalidOperationException>(() => account.Reparent(account));
    }

    // --- Activate / Deactivate ---

    [Fact]
    public void Deactivate_sets_IsActive_false()
    {
        var account = NewAnalytical();
        account.Deactivate();
        Assert.False(account.IsActive);
    }

    [Fact]
    public void Deactivate_makes_CanReceivePayable_false()
    {
        var account = NewAnalytical();
        account.Deactivate();
        Assert.False(account.CanReceivePayable);
    }

    [Fact]
    public void Activate_restores_IsActive_true()
    {
        var account = NewAnalytical();
        account.Deactivate();
        account.Activate();
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Activate_restores_CanReceivePayable()
    {
        var account = NewAnalytical();
        account.Deactivate();
        account.Activate();
        Assert.True(account.CanReceivePayable);
    }

    // --- Rename ---

    [Fact]
    public void Rename_changes_name()
    {
        var account = NewAnalytical();
        account.Rename("New Name");
        Assert.Equal("New Name", account.Name);
    }

    [Fact]
    public void Rename_trims_whitespace()
    {
        var account = NewAnalytical();
        account.Rename("  Trimmed  ");
        Assert.Equal("Trimmed", account.Name);
    }

    [Fact]
    public void Rename_throws_on_null()
    {
        var account = NewAnalytical();
        Assert.Throws<ArgumentNullException>(() => account.Rename(null!));
    }

    [Fact]
    public void Rename_throws_on_empty_string()
    {
        var account = NewAnalytical();
        Assert.Throws<ArgumentException>(() => account.Rename("   "));
    }
}
