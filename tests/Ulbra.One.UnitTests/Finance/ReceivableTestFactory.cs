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
}
