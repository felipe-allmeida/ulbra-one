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
    public DateTimeOffset CreatedAt { get; private set; }

    private PayableAttachment() { }
    public PayableAttachment(Guid payableId, string fileName, string contentType, long sizeBytes, string storagePath, Guid uploadedById, DateTimeOffset createdAt)
    {
        PayableId = payableId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedById = uploadedById;
        CreatedAt = createdAt;
    }
}
