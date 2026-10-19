using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>A requisition attachment to carry onto a payable — the source object-storage key plus the metadata
/// to reproduce it. Finance-local so the Application layer stays decoupled from Budgeting's messaging DTO; the
/// Infrastructure consumers map their <c>RequisitionAttachmentRef</c> to this (parsing the neutral Kind string
/// into <see cref="PayableAttachmentKind"/>).</summary>
public sealed record RequisitionAttachmentToCopy(
    string FileName, string ContentType, long SizeBytes, string SourceStorageKey, Guid UploadedById,
    PayableAttachmentKind Kind = PayableAttachmentKind.Other);

/// <summary>Carries a requisition's attachments onto its payable. Shared by the requisition-approval consumer
/// (files known at approval) and the post-approval attachment consumer (a late Nota Fiscal added by the
/// requester), so both reproduce the exact same copy behavior.</summary>
public interface IRequisitionAttachmentCopier
{
    /// <summary>Copies each source blob into a Finance-owned key and carries it onto <paramref name="payable"/> as
    /// a <see cref="PayableAttachment"/>. When <paramref name="promoteFiscalXml"/> is set (the creation path, on a
    /// fresh pre-incurral payable), the first <c>.xml</c> that ingests successfully instead becomes the payable's
    /// fiscal document. Post-approval callers pass <c>false</c>: a late file stays a plain attachment — enough to
    /// clear the "NF pendente" pendency, and it never mutates a classified/booked payable's type/issue date or its
    /// withholdings. Mutates the payable and adds any fiscal document to the ambient <see cref="IUnitOfWork"/>; the
    /// CALLER owns <c>SaveChanges</c>. Skips files already on the payable and missing source blobs, so at-least-once
    /// redelivery / duplicate uploads never pile up. Returns the number of files carried onto the payable.</summary>
    Task<int> CopyOntoPayableAsync(
        Payable payable, IReadOnlyList<RequisitionAttachmentToCopy> sources, Guid requisitionId, bool promoteFiscalXml, CancellationToken ct);
}

public sealed class RequisitionAttachmentCopier(
    IFileStorageService storage, IFiscalDocumentIngestionService fiscalIngestion, IUnitOfWork uow,
    ILogger<RequisitionAttachmentCopier> logger, TimeProvider clock)
    : IRequisitionAttachmentCopier
{
    public async Task<int> CopyOntoPayableAsync(
        Payable payable, IReadOnlyList<RequisitionAttachmentToCopy> sources, Guid requisitionId, bool promoteFiscalXml, CancellationToken ct)
    {
        if (sources.Count == 0) return 0;

        // Idempotency: skip a file already carried onto the payable in an earlier delivery (post-approval
        // redelivery, or the same NF uploaded twice). Match on (FileName, SizeBytes) against what is present
        // BEFORE this call — never within the batch, so the creation path still copies every authored file.
        var preExisting = payable.Attachments.Select(a => (a.FileName, a.SizeBytes)).ToHashSet();

        // Promote a Nota Fiscal XML only when asked AND while the payable is still pre-schedule: post-approval keeps
        // late files as plain attachments (promotion would move a booked payable's type/issue date and desync its
        // withholdings, and a promoted XML lives as a FiscalDocument — outside the attachment dedup above — so it
        // could not be deduped on redelivery). A payable holds a single fiscal document, so an existing one blocks
        // further promotion (checked once; the creation payable is fresh, so this is false there).
        var canPromote = promoteFiscalXml && payable.Status is PayableStatus.Received or PayableStatus.PendingClassification
            or PayableStatus.PendingApproval or PayableStatus.PendingAccounting;
        var hasFiscalDocument = canPromote && await uow.ReadSet<FiscalDocument>().AnyAsync(d => d.PayableId == payable.Id, ct);

        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var src in sources)
        {
            if (preExisting.Contains((src.FileName, src.SizeBytes))) continue;

            // Copy the blob into a new key so Finance owns an independent file — the requisition's blob lifecycle
            // can't break the payable's. A vanished source blob is skipped rather than failing the whole batch.
            await using var blob = await storage.GetAsync(src.SourceStorageKey, ct);
            if (blob is null) continue;
            var key = await storage.SaveAsync(src.FileName, blob, ct);

            // Only .xml is a fiscal-document candidate: parsed deterministically by namespace (never AI vision),
            // so requisition attachments can't trigger an AI cost. PDFs/images stay as regular attachments.
            if (canPromote && !hasFiscalDocument && src.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                var source = new FiscalDocumentSource(key, src.FileName, src.ContentType, src.SizeBytes, src.UploadedById);
                var ingested = await fiscalIngestion.IngestAsync(payable, source, preParsed: null, rejectEmptyExtraction: true, ct);
                if (ingested.IsSuccess) { hasFiscalDocument = true; added++; continue; }
                logger.LogWarning("Requisition {RequisitionId}: attachment {FileName} not promoted to fiscal document ({Error}); kept as a regular attachment.",
                    requisitionId, src.FileName, ingested.Error.Code);
            }

            // Add the attachment explicitly (Added state) rather than via payable.AddAttachment: on the
            // post-approval path the payable is loaded Unchanged, and appending to its tracked collection makes
            // EF classify the new child as Modified → an UPDATE of a non-existent row (0 rows → concurrency
            // fault). uow.AddAsync forces the INSERT and matches the Finance attachment-upload endpoint.
            await uow.AddAsync(new PayableAttachment(
                payable.Id, src.FileName, src.ContentType, src.SizeBytes, key, src.UploadedById, now, src.Kind), ct);
            added++;
        }

        return added;
    }
}
