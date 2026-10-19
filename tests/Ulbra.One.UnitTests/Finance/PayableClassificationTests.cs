using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PayableClassificationTests
{
    private static Payable NewPayable() =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Energia",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(100m, 1, new DateOnly(2026, 7, 10), 1));

    [Fact]
    public void Classify_sets_the_accounting_account()
    {
        var p = NewPayable();
        var acc = Guid.NewGuid();

        p.Classify(acc);

        Assert.Equal(acc, p.AccountingAccountId);
        Assert.True(p.IsClassified);
    }

    [Fact]
    public void Classify_with_null_clears_the_account()
    {
        var p = NewPayable();
        p.Classify(Guid.NewGuid());

        p.Classify(null);

        Assert.Null(p.AccountingAccountId);
        Assert.False(p.IsClassified);
    }

    [Fact]
    public void IsClassified_is_true_when_accounting_set()
    {
        var p = NewPayable();
        p.Classify(Guid.NewGuid());
        Assert.True(p.IsClassified);
    }

    [Fact]
    public void New_payable_is_not_classified()
    {
        Assert.False(NewPayable().IsClassified);
    }

    [Fact]
    public void Policy_defaults_both_flags_off()
    {
        var policy = new ClassificationPolicy(Guid.NewGuid());
        Assert.False(policy.RequireToApprove);
        Assert.False(policy.RequireToPay);
    }

    [Fact]
    public void Policy_update_sets_flags()
    {
        var policy = new ClassificationPolicy(Guid.NewGuid());
        policy.Update(requireToApprove: true, requireToPay: false);
        Assert.True(policy.RequireToApprove);
        Assert.False(policy.RequireToPay);
    }
}
