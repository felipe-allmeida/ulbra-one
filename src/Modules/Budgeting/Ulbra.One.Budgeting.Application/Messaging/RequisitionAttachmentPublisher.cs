using MassTransit;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Publishes <see cref="RequisitionAttachmentAdded"/> when a file is attached to a requisition that has
/// already generated a Payable (post-approval statuses) — so Finance carries the late Nota Fiscal onto the
/// existing Payable and clears its "NF pendente" pendency. No-op before approval: those files ride
/// <see cref="RequisitionApprovedForPayment"/> at approval instead, so callers invoke it unconditionally after
/// persisting the attachment. Publish rides the EF bus outbox, committing atomically with <c>SaveChangesAsync</c>.</summary>
public static class RequisitionAttachmentPublisher
{
    public static Task PublishIfPostApproval(
        this Requisition requisition, RequisitionAttachment attachment, IPublishEndpoint publish, DateTimeOffset occurredAt, CancellationToken ct) =>
        requisition.Status is RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument
            or RequisitionStatus.Paid or RequisitionStatus.Finalized
            ? publish.Publish(
                new RequisitionAttachmentAdded(requisition.Id, requisition.CompanyId,
                    new RequisitionAttachmentRef(attachment.FileName, attachment.ContentType, attachment.SizeBytes,
                        attachment.StoragePath, attachment.UploadedById, attachment.Kind.ToString()),
                    occurredAt), ct)
            : Task.CompletedTask;
}
