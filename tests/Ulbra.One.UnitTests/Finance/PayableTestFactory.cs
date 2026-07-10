using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Builds real <see cref="Payable"/> aggregates in well-known states for unit tests.
/// Uses the real constructor and domain method signatures — never bypasses domain logic.
/// </summary>
public static class PayableTestFactory
{
    private static Payable BasePayable() =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "Fornecedor Teste",
            documentNumber: "NF-001", issueDate: new DateOnly(2026, 6, 1), description: "Teste",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(100m, 1, new DateOnly(2026, 7, 1), 1));

    /// <summary>Payable in the <c>Received</c> state (created, not yet classified or approved).</summary>
    public static Payable Received(string actorId, DateTimeOffset createdAt)
    {
        var p = BasePayable();
        p.MarkCreated(actorId, createdAt);
        return p;
    }

    /// <summary>
    /// Payable in the <c>Scheduled</c> state (created → classified → approved).
    /// The <c>Classified</c> event has no dedicated entity timestamp; the builder uses <c>UpdatedAt</c>,
    /// which is set by <c>MarkCreated</c> and not altered by <c>Classify</c>/<c>Approve</c> here
    /// (unit tests don't call <c>MarkUpdated</c>), so Classified and Created share the same timestamp.
    /// </summary>
    public static Payable Scheduled(string actorId, DateTimeOffset createdAt, DateTimeOffset approvedAt,
        Guid accountingAccountId)
    {
        var p = BasePayable();
        p.MarkCreated(actorId, createdAt);
        p.Classify(accountingAccountId);
        p.Approve(actorId, approvedAt);
        // Approval now parks the payable in PendingAccounting; completing the accounting review
        // (with a registered fiscal document) is what schedules it.
        p.CompleteAccounting(actorId, approvedAt, hasFiscalDocument: true);
        return p;
    }

    /// <summary>Payable in the <c>Cancelled</c> state (created → cancelled with reason).</summary>
    public static Payable Cancelled(DateTimeOffset createdAt, string reason)
    {
        var p = BasePayable();
        p.MarkCreated("system", createdAt);
        p.Cancel(reason);
        return p;
    }

    /// <summary>
    /// Payable in the <c>Received</c> state with one attachment, for testing attachment events.
    /// </summary>
    public static Payable ReceivedWithAttachment(string actorId, DateTimeOffset createdAt,
        Guid uploaderId, string fileName, DateTimeOffset uploadedAt)
    {
        var p = BasePayable();
        p.MarkCreated(actorId, createdAt);
        p.AddAttachment(new PayableAttachment(p.Id, fileName, "application/xml", 1024L, $"path/{fileName}", uploaderId, uploadedAt));
        return p;
    }
}
