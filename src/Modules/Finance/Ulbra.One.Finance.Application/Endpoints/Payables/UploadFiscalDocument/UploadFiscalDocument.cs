using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadFiscalDocument;

/// <summary>
/// Persists the fiscal document of record for a payable: stores the raw file in object storage and the
/// parsed fields + full extraction in <c>finance.fiscal_document</c> (one per payable, re-upload replaces).
/// The payable's own fields are set through create/update, not here.
/// </summary>
public sealed class UploadFiscalDocument : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/fiscal-document", Handle).WithName("UploadPayableFiscalDocument").WithTags("Finance")
            .RequireAuthorization().DisableAntiforgery()
            .Produces<FiscalDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        IFormFile file,
        UserContext user,
        IFiscalDocumentReader reader,
        IFileStorageService storage,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return Result.Failure<FiscalDocumentResponse>(Error.NotFound("Payable.NotFound", "Payable not found.")).ToOk();

        if (file.Length == 0)
            return Result.Failure<FiscalDocumentResponse>(Error.Validation("File.Empty", "File is empty.")).ToOk();
        if (file.Length > MaxFileSizeBytes)
            return Result.Failure<FiscalDocumentResponse>(
                Error.Validation("File.TooLarge", $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.")).ToOk();

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return Result.Failure<FiscalDocumentResponse>(validation.Error).ToOk();

        ParsedFiscalDocument? parsed;
        try
        {
            parsed = await reader.ReadAsync(file.FileName, stream, ct);
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

        if (stream.CanSeek) stream.Position = 0;
        var key = await storage.SaveAsync(file.FileName, stream, ct);

        var uploadedById = Guid.Parse(user.UserId);
        var doc = new FiscalDocument(payable.Id, parsed.Format, parsed.AccessKey, parsed.DocumentNumber,
            parsed.IssueDate, parsed.TotalAmount, parsed.IssuerDocument, parsed.IssuerName, parsed.RecipientDocument,
            parsed.RawExtractionJson, file.FileName, file.ContentType, file.Length, key, uploadedById,
            timeProvider.GetUtcNow(),
            cfops: parsed.Fiscal?.Cfops ?? [],
            ncms: parsed.Fiscal?.Ncms ?? [],
            icmsAmount: parsed.Fiscal?.Icms,
            ipiAmount: parsed.Fiscal?.Ipi,
            pisAmount: parsed.Fiscal?.Pis,
            cofinsAmount: parsed.Fiscal?.Cofins,
            totalTaxesAmount: parsed.Fiscal?.TotalTaxes);
        await uow.AddAsync(doc, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/fiscal-document",
            FiscalDocumentResponse.From(doc));
    }
}
