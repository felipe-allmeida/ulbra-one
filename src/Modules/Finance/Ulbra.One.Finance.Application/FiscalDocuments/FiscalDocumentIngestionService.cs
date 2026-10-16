using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>The stored file to turn into a payable's fiscal document — the object-storage key plus the
/// metadata to persist alongside the parsed fields.</summary>
public sealed record FiscalDocumentSource(string StorageKey, string FileName, string ContentType, long SizeBytes, Guid UploadedById);

/// <summary>Turns a stored file into a payable's <see cref="FiscalDocument"/>: read → parse → build → add to
/// the unit of work, applying the same Service/Product type precedence the manual upload uses. Shared by the
/// upload endpoint and the requisition-approval consumer (which promotes a Nota Fiscal attachment).
/// <para>Deliberately does NOT call <c>SaveChanges</c> or recompute withholdings — the caller owns the
/// transactional boundary. In the consumer this is essential: the FiscalDocument must be added to the SAME
/// unit of work as the Payable + the outbox message and committed once, so withholdings (which read the
/// document by query) are recomputed later by the PayableReceived consumer, after the commit.</para></summary>
public interface IFiscalDocumentIngestionService
{
    Task<Result<FiscalDocument>> IngestAsync(Payable payable, FiscalDocumentSource source, ParsedFiscalDocument? preParsed, bool rejectEmptyExtraction, CancellationToken ct);
}

public sealed class FiscalDocumentIngestionService(
    IFileStorageService storage, IFiscalDocumentReader reader, IUnitOfWork uow, TimeProvider clock)
    : IFiscalDocumentIngestionService
{
    public async Task<Result<FiscalDocument>> IngestAsync(
        Payable payable, FiscalDocumentSource source, ParsedFiscalDocument? preParsed, bool rejectEmptyExtraction, CancellationToken ct)
    {
        var parsed = preParsed;
        if (parsed is null)
        {
            await using var stream = await storage.GetAsync(source.StorageKey, ct);
            if (stream is null)
                return Result.Failure<FiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.NotFound, "Uploaded file not found."));

            using var capped = await FiscalDocumentLimits.ReadCappedAsync(stream, ct);
            if (capped is null)
                return Result.Failure<FiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.TooLarge,
                    $"File exceeds the {FiscalDocumentLimits.MaxBytes / (1024 * 1024)}MB limit."));

            try
            {
                // The reader throws on malformed content (e.g. a file marked as NF that isn't valid XML). Catch
                // it here so a bad attachment becomes a failure the caller can degrade on — never an unhandled
                // exception that would fault the whole consumer and block the Payable.
                parsed = await reader.ReadAsync(source.FileName, capped, ct);
            }
            catch
            {
                return Result.Failure<FiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.ParseFailed, "Could not parse the fiscal document."));
            }
        }

        if (parsed is null)
            return Result.Failure<FiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.UnsupportedFormat, "Unsupported fiscal document format."));

        if (rejectEmptyExtraction && IsEmptyExtraction(parsed))
            return Result.Failure<FiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.EmptyExtraction, "No fiscal fields could be extracted."));

        var doc = new FiscalDocument(payable.Id, parsed.Format, parsed.AccessKey, parsed.DocumentNumber,
            parsed.IssueDate, parsed.TotalAmount, parsed.IssuerDocument, parsed.IssuerName, parsed.RecipientDocument,
            parsed.RawExtractionJson, source.FileName, source.ContentType, source.SizeBytes, source.StorageKey, source.UploadedById,
            clock.GetUtcNow(),
            cfops: parsed.Fiscal?.Cfops ?? [],
            ncms: parsed.Fiscal?.Ncms ?? [],
            icmsAmount: parsed.Fiscal?.Icms,
            ipiAmount: parsed.Fiscal?.Ipi,
            pisAmount: parsed.Fiscal?.Pis,
            cofinsAmount: parsed.Fiscal?.Cofins,
            totalTaxesAmount: parsed.Fiscal?.TotalTaxes,
            lc116Subitem: parsed.Lc116Subitem,
            municipalServiceCode: parsed.MunicipalServiceCode,
            serviceMunicipalityIbge: parsed.ServiceMunicipalityIbge,
            cnae: parsed.CnaeCode,
            invoiceIssRate: parsed.InvoiceIssRate,
            issWithheldOnInvoice: parsed.IssWithheldOnInvoice);
        await uow.AddAsync(doc, ct);

        // Type precedence (mirrors the manual upload): XML format is authoritative (NFS-e=Service, NF-e=Product);
        // a PDF/image guess only fills an undetermined type and never overrides a human/requisition choice.
        var derivedType = DeriveType(parsed);
        try
        {
            if (parsed.Format is FiscalDocumentFormat.NfseXml or FiscalDocumentFormat.NfeXml)
                payable.SetType(derivedType);
            else if (payable.Type is null && derivedType is not null)
                payable.SetType(derivedType);

            // The Nota Fiscal's own emission date is the authoritative data de emissão — apply it to the payable
            // (the incurral uses IssueDate as the accounting competence). This overrides the default issue date
            // derived from the requisition's competency once a real NF is read.
            if (parsed.IssueDate is { } nfIssueDate)
                payable.SetIssueDate(nfIssueDate);
        }
        catch (InvalidOperationException) { /* payable in a terminal state: keep the existing type / issue date */ }

        // Propagate the parsed número do documento onto the payable — it lives on the FiscalDocument, but Contábil
        // and the reports read it from Payable.DocumentNumber. Outside the try above (and unguarded in the domain)
        // so it lands even on a paid payable, where type/issue date stay locked. Fill only when empty so a manually
        // entered/corrected número is never clobbered by a later re-ingestion.
        if (string.IsNullOrWhiteSpace(payable.DocumentNumber) && !string.IsNullOrWhiteSpace(parsed.DocumentNumber))
            payable.SetDocumentNumber(parsed.DocumentNumber);

        return Result.Success(doc);
    }

    /// <summary>"Empty" = the parser / AI vision couldn't pull any identifying fiscal field. Guards the consumer
    /// (rejectEmptyExtraction=true) against creating a useless FiscalDocument — which would occupy the payable's
    /// single slot — when AI vision is off or failed on a PDF/image.</summary>
    private static bool IsEmptyExtraction(ParsedFiscalDocument p) =>
        string.IsNullOrWhiteSpace(p.DocumentNumber) && string.IsNullOrWhiteSpace(p.AccessKey)
        && string.IsNullOrWhiteSpace(p.IssuerDocument) && p.TotalAmount is null && p.IssueDate is null
        && p.Fiscal?.Cfops is not { Count: > 0 } && p.Fiscal?.Ncms is not { Count: > 0 }
        && string.IsNullOrWhiteSpace(p.Lc116Subitem);

    private static PayableType? DeriveType(ParsedFiscalDocument parsed) => parsed.Format switch
    {
        FiscalDocumentFormat.NfseXml => PayableType.Service,
        FiscalDocumentFormat.NfeXml => PayableType.Product,
        _ => parsed.SuggestedType
           ?? (parsed.Lc116Subitem is not null ? PayableType.Service
              : parsed.Fiscal?.Cfops is { Count: > 0 } || parsed.Fiscal?.Ncms is { Count: > 0 } ? PayableType.Product
              : (PayableType?)null),
    };
}
