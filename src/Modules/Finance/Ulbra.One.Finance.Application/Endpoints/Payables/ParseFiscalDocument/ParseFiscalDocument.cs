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
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ParseFiscalDocument;

/// <summary>
/// Stateless parse/preview: reads a blob already in object storage by key, detects the fiscal document
/// format, extracts its fields and suggests a vendor by issuer CNPJ.
/// Persists nothing and mutates no payable — it only feeds the create/edit form.
/// </summary>
public sealed class ParseFiscalDocument : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/fiscal-document/parse", Handle).WithName("ParseFiscalDocument").WithTags("Finance")
            .WithSummary("Parses a fiscal document without saving it.")
            .WithDescription("Reads a blob already in storage by Key, detects the document format, extracts its fields and "
                + "suggests a vendor by matching the issuer CNPJ. Persists nothing and mutates no payable - it "
                + "exists to prefill the create or edit form, so calling it twice is free.")
            .RequirePermission(Permissions.PayablesWrite)
            .Produces<ParsedFiscalDocument>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company the document belongs to. Required, and enforced twice: the caller must "
            + "have access to it, and the storage key must sit under that company's prefix — so a key "
            + "from another company is refused with 403 rather than parsed.")]
        CompanyId companyId,
        ParseFiscalDocumentRequest req,
        UserContext user,
        IFileStorageService storage,
        IFiscalDocumentReader reader,
        IMemoryCache cache,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // Only those with access to the company read its blobs; and the key must belong to the company's
        // namespace (prevents reading another company's blob via an arbitrary key). Both before touching storage.
        if (!user.CanAccess(companyId.Value))
            return Result.Failure<ParsedFiscalDocument>(
                Error.Forbidden(ErrorCodes.FiscalDocument.CompanyForbidden, "No access to this company.")).ToOk();

        if (!FiscalDocumentKey.BelongsTo(req.Key, companyId.Value))
            return Result.Failure<ParsedFiscalDocument>(
                Error.NotFound(ErrorCodes.FiscalDocument.NotFound, "Uploaded file not found.")).ToOk();

        await using var stream = await storage.GetAsync(req.Key, ct);
        if (stream is null)
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation(ErrorCodes.FiscalDocument.NotFound, "Uploaded file not found.")).ToOk();

        using var capped = await FiscalDocumentLimits.ReadCappedAsync(stream, ct);
        if (capped is null)
            return Result.Failure<ParsedFiscalDocument>(Error.Validation(ErrorCodes.FiscalDocument.TooLarge,
                $"File exceeds the {FiscalDocumentLimits.MaxBytes / (1024 * 1024)}MB limit.")).ToOk();

        ParsedFiscalDocument? parsed;
        try
        {
            parsed = await reader.ReadAsync(req.FileName, capped, ct);
        }
        catch
        {
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation(ErrorCodes.FiscalDocument.ParseFailed, "Could not parse the fiscal document.")).ToOk();
        }

        if (parsed is null)
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation(ErrorCodes.FiscalDocument.UnsupportedFormat, "Unsupported fiscal document format.")).ToOk();

        // Extract-once: cache the raw extraction keyed by the blob so the subsequent save (UploadFiscalDocument)
        // reuses it instead of running a 2nd (paid) AI vision extraction of the same PDF/image. Store BEFORE the
        // vendor-match augmentation below (preview-only display concern). Best-effort, per-instance, 1h TTL.
        cache.Set($"fiscaldoc:parse:{req.Key}", parsed, TimeSpan.FromHours(1));

        // Suggest a vendor by issuer document (vendors are global — document is globally unique); the form
        // fills VendorId only when still empty.
        if (parsed.IssuerDocument is { Length: > 0 } issuerDocument)
        {
            var vendor = await uow.ReadSet<VendorEntity>()
                .FirstOrDefaultAsync(v => v.Document == issuerDocument, ct);
            if (vendor is not null)
                parsed = parsed with { MatchedVendorId = new VendorId(vendor.Id), MatchedVendorName = vendor.Name };
        }

        return Result.Success(parsed).ToOk();
    }
}

public sealed record ParseFiscalDocumentRequest(
    [property: SwaggerSchema("Storage key returned by the upload-url request, pointing at the blob "
        + "already uploaded.")]
    string Key,
    [property: SwaggerSchema("Original file name, used to help detect the document format.")]
    [property: SwaggerExample("nfse-12345.xml")]
    string FileName);
