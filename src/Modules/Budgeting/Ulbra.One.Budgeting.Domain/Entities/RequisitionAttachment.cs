using Ulbra.One.Budgeting.Domain;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>Metadata for a file (quote, contract, invoice) stored in object storage, linked to a
/// requisition. The bytes live in <c>IFileStorageService</c>; only the storage key is persisted here.
/// <see cref="Kind"/> marks whether the file is a Nota Fiscal, so Finance can promote it to the Payable's
/// fiscal document on approval.</summary>
public class RequisitionAttachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequisitionId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public Guid UploadedById { get; private set; }
    public RequisitionAttachmentKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RequisitionAttachment() { }

    public RequisitionAttachment(Guid requisitionId, string fileName, string contentType, long sizeBytes, string storagePath, Guid uploadedById, DateTimeOffset createdAt, RequisitionAttachmentKind kind = RequisitionAttachmentKind.Other)
    {
        RequisitionId = requisitionId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedById = uploadedById;
        Kind = kind;
        CreatedAt = createdAt;
    }

    /// <summary>Reclassifies the file's kind — the requester/controllership fixing a mis-typed attachment
    /// (e.g. a boleto read as Other) while editing the requisition (AEL-959).</summary>
    public void SetKind(RequisitionAttachmentKind kind) => Kind = kind;
}
