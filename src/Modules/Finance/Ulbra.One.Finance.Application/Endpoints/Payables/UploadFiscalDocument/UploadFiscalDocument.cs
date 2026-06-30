using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadFiscalDocument;

/// <summary>
/// Persists the fiscal document of record for a payable: re-parses the blob already in object storage
/// (keyed by <c>Key</c>) and writes the parsed fields + stored-file metadata to
/// <c>finance.fiscal_document</c> (one per payable, re-upload replaces).
/// Does NOT call <c>SaveAsync</c> — the client uploaded the file directly via the presigned URL,
/// or a prior multipart-to-storage step placed it there.
/// </summary>
public sealed class UploadFiscalDocument : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/fiscal-document", Handle).WithName("UploadPayableFiscalDocument").WithTags("Finance")
            .RequireAuthorization()
            .Produces<FiscalDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        UploadFiscalDocumentRequest req,
        UserContext user,
        IFiscalDocumentReader reader,
        IFileStorageService storage,
        IUnitOfWork uow,
        IAutoWithholdingService autoWithholding,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (req.SizeBytes <= 0 || string.IsNullOrWhiteSpace(req.ContentType) || string.IsNullOrWhiteSpace(req.FileName))
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation("FiscalDocument.InvalidMetadata", "fileName, contentType and sizeBytes are required.")).ToOk();

        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return Result.Failure<FiscalDocumentResponse>(Error.NotFound("Payable.NotFound", "Payable not found.")).ToOk();

        await using var stream = await storage.GetAsync(req.Key, ct);
        if (stream is null)
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation("FiscalDocument.NotFound", "Uploaded file not found.")).ToOk();

        using var capped = await FiscalDocumentLimits.ReadCappedAsync(stream, ct);
        if (capped is null)
            return Result.Failure<FiscalDocumentResponse>(Error.Validation("FiscalDocument.TooLarge",
                $"File exceeds the {FiscalDocumentLimits.MaxBytes / (1024 * 1024)}MB limit.")).ToOk();

        // v1 simplification: re-parse the blob on save rather than round-tripping the preview result
        // through the client. For XML this is free + deterministic; for PDF/image it costs a second AI
        // extraction whose fiscal metadata (CFOP/NCM/tax totals — informational, feeds classification) may
        // differ slightly from the preview. The payable's own fields are set via CreatePayable from the
        // user-reviewed form, so this never affects user-confirmed data. Follow-up: extract-once if PDF cost matters.
        ParsedFiscalDocument? parsed;
        try
        {
            parsed = await reader.ReadAsync(req.FileName, capped, ct);
        }
        catch
        {
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation("FiscalDocument.ParseFailed", "Could not parse the fiscal document.")).ToOk();
        }

        if (parsed is null)
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation("FiscalDocument.UnsupportedFormat", "Unsupported fiscal document format.")).ToOk();

        // Replace any existing fiscal document first (unique per payable) to avoid the unique-index conflict.
        var existing = await uow.Set<FiscalDocument>().Where(d => d.PayableId == payable.Id).ToListAsync(ct);
        if (existing.Count > 0)
        {
            uow.RemoveRange(existing);
            await uow.SaveChangesAsync(ct);
        }

        var uploadedById = Guid.Parse(user.UserId);
        var doc = new FiscalDocument(payable.Id, parsed.Format, parsed.AccessKey, parsed.DocumentNumber,
            parsed.IssueDate, parsed.TotalAmount, parsed.IssuerDocument, parsed.IssuerName, parsed.RecipientDocument,
            parsed.RawExtractionJson, req.FileName, req.ContentType, req.SizeBytes, req.Key, uploadedById,
            timeProvider.GetUtcNow(),
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
            notaIssRate: parsed.NotaIssRate,
            issRetidoNaNota: parsed.IssRetidoNaNota);
        await uow.AddAsync(doc, ct);
        await uow.SaveChangesAsync(ct);

        // Recompute withholdings now that the NFS-e service signals (LC116/município) are persisted — the
        // auto-calc at classification time may have run before this document was linked. No-op when the payable
        // is already Scheduled/Paid (locked) or when the document has no LC116 subitem (NF-e product).
        await autoWithholding.RecalculateAndApplyAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/fiscal-document",
            FiscalDocumentResponse.From(doc));
    }
}

public sealed record UploadFiscalDocumentRequest(string Key, string FileName, string ContentType, long SizeBytes);
