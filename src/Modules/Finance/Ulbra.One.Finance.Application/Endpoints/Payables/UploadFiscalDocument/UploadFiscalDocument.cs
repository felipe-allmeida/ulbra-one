using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
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
            .WithSummary("Registers the payable\u0027s fiscal document.")
            .WithDescription("Records the nota fiscal of record for a payable: re-parses the blob already in storage and "
                + "saves the parsed fields with the file metadata. One per payable - re-uploading REPLACES the "
                + "previous one. The document format is authoritative over the payable type: an NFS-e makes it "
                + "a service, which is what decides whether source withholdings apply. The file must already be "
                + "in storage, via the presigned upload URL.")
            .RequirePermission(Permissions.PayablesWrite)
            .Produces<FiscalDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        UploadFiscalDocumentRequest req,
        UserContext user,
        IFiscalDocumentIngestionService ingestion,
        IMemoryCache cache,
        IUnitOfWork uow,
        IAutoWithholdingService autoWithholding,
        CancellationToken ct)
    {
        if (req.SizeBytes <= 0 || string.IsNullOrWhiteSpace(req.ContentType) || string.IsNullOrWhiteSpace(req.FileName))
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation(ErrorCodes.FiscalDocument.InvalidMetadata, "fileName, contentType and sizeBytes are required.")).ToOk();

        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return Result.Failure<FiscalDocumentResponse>(Error.NotFound(ErrorCodes.Payable.NotFound, "Payable not found.")).ToOk();

        // Per-resource authorization: company scope + the ingested key must belong to the payable's company
        // (prevents linking another company's blob to the payable). Uniform 404 (masks existence).
        if (!user.CanAccess(payable.CompanyId) || !FiscalDocumentKey.BelongsTo(req.Key, payable.CompanyId))
            return Result.Failure<FiscalDocumentResponse>(Error.NotFound(ErrorCodes.Payable.NotFound, "Payable not found.")).ToOk();

        // Extract-once: reuse the preview's extraction of this same blob (cached by ParseFiscalDocument under
        // the blob key) instead of running a second (paid) AI vision extraction for PDF/image — the save then
        // persists exactly what the user reviewed. Cache miss (save without a preview, or a different instance)
        // → the ingestion service extracts now; XML is free either way.
        cache.TryGetValue($"fiscaldoc:parse:{req.Key}", out ParsedFiscalDocument? preParsed);

        // Replace any existing fiscal document first (unique per payable) to avoid the unique-index conflict.
        // This needs its own SaveChanges (EF doesn't guarantee DELETE-before-INSERT within one) — safe here in
        // the endpoint, which owns the transactional boundary (unlike the consumer).
        var existing = await uow.Set<FiscalDocument>().Where(d => d.PayableId == payable.Id).ToListAsync(ct);
        if (existing.Count > 0)
        {
            uow.RemoveRange(existing);
            await uow.SaveChangesAsync(ct);
        }

        var source = new FiscalDocumentSource(req.Key, req.FileName, req.ContentType, req.SizeBytes, Guid.Parse(user.UserId));
        // rejectEmptyExtraction: false — the manual flow persists even an empty extraction the user confirmed
        // in the preview (parity with the prior behavior).
        var ingested = await ingestion.IngestAsync(payable, source, preParsed, rejectEmptyExtraction: false, ct);
        if (ingested.IsFailure)
            return Result.Failure<FiscalDocumentResponse>(ingested.Error).ToOk();

        await uow.SaveChangesAsync(ct);

        // Recompute withholdings now that the document + service-nature are persisted — the auto-calc at
        // classification time may have run before this document was linked. No-op when the payable is already
        // Scheduled/Paid (locked); an NF-e over a former service clears the stale rows (Product withholds nothing).
        await autoWithholding.RecalculateAndApplyAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/fiscal-document",
            FiscalDocumentResponse.From(ingested.Value));
    }
}

public sealed record UploadFiscalDocumentRequest(
    [property: SwaggerSchema("Storage key of the blob already uploaded through the presigned URL.")]
    string Key,
    [property: SwaggerSchema("Original file name, kept as the document\u0027s display name.")]
    [property: SwaggerExample("nfse-12345.xml")]
    string FileName,
    [property: SwaggerSchema("MIME type of the uploaded file.")]
    [property: SwaggerExample("application/xml")]
    string ContentType,
    [property: SwaggerSchema("Size of the uploaded file in bytes.")]
    [property: SwaggerExample("48213")]
    long SizeBytes);
