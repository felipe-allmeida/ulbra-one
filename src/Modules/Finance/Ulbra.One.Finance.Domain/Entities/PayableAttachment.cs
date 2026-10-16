namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Metadata for a file (NF/boleto) stored in object storage, linked to a payable.</summary>
public class PayableAttachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PayableId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public Guid UploadedById { get; private set; }

    /// <summary>What the file is (carried from the requisition or chosen at upload). Only <see cref="PayableAttachmentKind.FiscalDocument"/> clears the "NF presente" gate.</summary>
    public PayableAttachmentKind Kind { get; private set; } = PayableAttachmentKind.Other;
    public DateTimeOffset CreatedAt { get; private set; }

    private PayableAttachment() { }
    public PayableAttachment(Guid payableId, string fileName, string contentType, long sizeBytes, string storagePath, Guid uploadedById, DateTimeOffset createdAt, PayableAttachmentKind kind = PayableAttachmentKind.Other)
    {
        PayableId = payableId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedById = uploadedById;
        Kind = kind;
        CreatedAt = createdAt;
    }
}
