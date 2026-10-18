using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Builds real <see cref="Receivable"/> aggregates in well-known states for unit tests.
/// Uses the real constructor and domain method signatures — never bypasses domain logic.
/// </summary>
public static class ReceivableTestFactory
{
    private static Receivable BaseReceivable() =>
        new(Guid.NewGuid(), ReceivableOrigin.Manual, Guid.NewGuid(), "Cliente Teste",
            documentNumber: null, issueDate: new DateOnly(2026, 6, 1), description: "Teste",
            costCenterId: Guid.NewGuid(), revenueCategoryId: null, ledgerAccountId: null,
            Receivable.GenerateInstallments(100m, 1, new DateOnly(2026, 7, 1), 1));

    /// <summary>Receivable in the <c>AwaitingReceipt</c> state (created, no receipts).</summary>
    public static Receivable Pending(string createdActorId, DateTimeOffset createdAt)
    {
        var r = BaseReceivable();
        r.MarkCreated(createdActorId, createdAt);
        return r;
    }

    /// <summary>Receivable in the <c>Cancelled</c> state.</summary>
    public static Receivable Cancelled(string createdActorId, DateTimeOffset createdAt,
        Guid cancelledById, DateTimeOffset cancelledAt, string reason)
    {
        var r = BaseReceivable();
        r.MarkCreated(createdActorId, createdAt);
        r.Cancel(cancelledById, cancelledAt, reason);
        return r;
    }

    /// <summary>Receivable in the <c>WrittenOff</c> state.</summary>
    public static Receivable WrittenOff(string createdActorId, DateTimeOffset createdAt, string? reason)
    {
        var r = BaseReceivable();
        r.MarkCreated(createdActorId, createdAt);
        r.MarkUpdated(createdActorId, createdAt.AddDays(1));
        r.WriteOff(reason);
        return r;
    }

    /// <summary>Receivable with a single non-reversed receipt on the first installment.</summary>
    public static Receivable WithReceipt(string createdActorId, DateTimeOffset createdAt,
        Guid receiptCreatedById, DateTimeOffset receiptCreatedAt, decimal receiptAmount = 100m)
    {
        var r = BaseReceivable();
        r.MarkCreated(createdActorId, createdAt);
        var installment = r.Installments[0];
        var receipt = new Receipt(
            installmentId: installment.Id,
            receivableId: r.Id,
            receiptDate: DateOnly.FromDateTime(receiptCreatedAt.Date),
            amount: receiptAmount,
            bankAccountId: Guid.NewGuid(),
            method: PaymentMethod.Pix,
            interest: 0m, fine: 0m, discount: 0m,
            notes: null,
            createdById: receiptCreatedById,
            createdAt: receiptCreatedAt);
        installment.RegisterReceipt(receipt);
        r.RecomputeSettlement();
        return r;
    }

    /// <summary>Receivable with a single receipt that has been reversed on the first installment.</summary>
    public static Receivable WithReversedReceipt(string createdActorId, DateTimeOffset createdAt,
        Guid receiptCreatedById, DateTimeOffset receiptCreatedAt,
        Guid reversedById, DateTimeOffset reversedAt, string? reversalReason,
        decimal receiptAmount = 100m)
    {
        var r = BaseReceivable();
        r.MarkCreated(createdActorId, createdAt);
        var installment = r.Installments[0];
        var receipt = new Receipt(
            installmentId: installment.Id,
            receivableId: r.Id,
            receiptDate: DateOnly.FromDateTime(receiptCreatedAt.Date),
            amount: receiptAmount,
            bankAccountId: Guid.NewGuid(),
            method: PaymentMethod.Pix,
            interest: 0m, fine: 0m, discount: 0m,
            notes: null,
            createdById: receiptCreatedById,
            createdAt: receiptCreatedAt);
        installment.RegisterReceipt(receipt);
        installment.ReverseReceipt(receipt.Id, reversedById, reversedAt, reversalReason);
        r.RecomputeSettlement();
        return r;
    }
}
