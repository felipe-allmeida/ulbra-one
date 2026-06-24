namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>
/// The fiscal document (NF-e XML, NFS-e XML or a mocked service PDF) of record for a payable: the raw
/// file is kept in object storage and the parsed fields + full extraction are stored here. One per payable.
/// </summary>
public class FiscalDocument
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PayableId { get; private set; }
    public FiscalDocumentFormat Format { get; private set; }

    /// <summary>NF-e access key (44 digits); null for NFS-e and the PDF mock.</summary>
    public string? AccessKey { get; private set; }
    public string? DocumentNumber { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public decimal? TotalAmount { get; private set; }

    /// <summary>Issuer (prestador/emitente) document, normalized to digits only.</summary>
    public string? IssuerDocument { get; private set; }
    public string? IssuerName { get; private set; }

    /// <summary>Recipient (tomador/destinatário) document, normalized to digits only.</summary>
    public string? RecipientDocument { get; private set; }

    /// <summary>Full parsed extraction as JSON, for fields not promoted to columns.</summary>
    public string RawExtractionJson { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public Guid UploadedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Fiscal signals (NF-e only): distinct CFOPs/NCMs across items + ICMSTot tax totals.
    public IReadOnlyList<string> Cfops { get; private set; } = [];
    public IReadOnlyList<string> Ncms { get; private set; } = [];
    public decimal? IcmsAmount { get; private set; }
    public decimal? IpiAmount { get; private set; }
    public decimal? PisAmount { get; private set; }
    public decimal? CofinsAmount { get; private set; }
    public decimal? TotalTaxesAmount { get; private set; }

    private FiscalDocument() { }

    public FiscalDocument(Guid payableId, FiscalDocumentFormat format, string? accessKey, string? documentNumber,
        DateOnly? issueDate, decimal? totalAmount, string? issuerDocument, string? issuerName,
        string? recipientDocument, string rawExtractionJson, string fileName, string contentType, long sizeBytes,
        string storagePath, Guid uploadedById, DateTimeOffset createdAt,
        IReadOnlyList<string>? cfops = null, IReadOnlyList<string>? ncms = null,
        decimal? icmsAmount = null, decimal? ipiAmount = null, decimal? pisAmount = null,
        decimal? cofinsAmount = null, decimal? totalTaxesAmount = null)
    {
        PayableId = payableId;
        Format = format;
        AccessKey = accessKey;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        TotalAmount = totalAmount;
        IssuerDocument = issuerDocument;
        IssuerName = issuerName;
        RecipientDocument = recipientDocument;
        RawExtractionJson = rawExtractionJson;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedById = uploadedById;
        CreatedAt = createdAt;
        Cfops = cfops ?? [];
        Ncms = ncms ?? [];
        IcmsAmount = icmsAmount;
        IpiAmount = ipiAmount;
        PisAmount = pisAmount;
        CofinsAmount = cofinsAmount;
        TotalTaxesAmount = totalTaxesAmount;
    }
}
