using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Guards the distinction that keeps "desfazer conciliação" safe: a transaction reconciled by CREATING a payment
/// may have that payment reversed on undo, while one that merely LINKED a pre-existing baixa (CNAB return) must
/// never be — the conciliation did not create that money movement, the bank did.
/// </summary>
public class BankTransactionLinkTests
{
    private static BankTransaction Txn() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "FITID-1",
        new DateOnly(2026, 8, 25), -150m, "PAGAMENTO DE TITULO", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Linking_an_existing_payment_flags_it_as_preexisting()
    {
        var t = Txn();
        var payment = Guid.NewGuid();

        t.LinkExistingPayment(payment);

        Assert.Equal(MatchStatus.Reconciled, t.MatchStatus);
        Assert.Equal(payment, t.MatchedPaymentId);
        Assert.True(t.PaymentPreexisting);
    }

    [Fact]
    public void Reconciling_through_the_conciliation_does_not_flag_it()
    {
        var t = Txn();

        t.Reconcile(Guid.NewGuid());

        Assert.Equal(MatchStatus.Reconciled, t.MatchStatus);
        Assert.False(t.PaymentPreexisting);
    }

    [Fact]
    public void Unreconciling_clears_the_flag_with_everything_else()
    {
        var t = Txn();
        t.LinkExistingPayment(Guid.NewGuid());

        t.Unreconcile();

        Assert.Equal(MatchStatus.Unmatched, t.MatchStatus);
        Assert.Null(t.MatchedPaymentId);
        Assert.False(t.PaymentPreexisting);
    }

    [Fact]
    public void A_reconciled_transaction_cannot_be_linked_again()
    {
        var t = Txn();
        t.Reconcile(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => t.LinkExistingPayment(Guid.NewGuid()));
    }
}
