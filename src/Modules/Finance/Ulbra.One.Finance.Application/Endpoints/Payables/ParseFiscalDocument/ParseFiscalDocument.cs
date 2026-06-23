using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ParseFiscalDocument;

/// <summary>
/// Stateless parse/preview: detects the fiscal document format, extracts its fields and suggests a vendor
/// by issuer CNPJ. Persists nothing and mutates no payable — it only feeds the create/edit form.
/// </summary>
public sealed class ParseFiscalDocument : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/fiscal-document/parse", Handle).WithName("ParseFiscalDocument").WithTags("Finance")
            .RequireAuthorization().DisableAntiforgery()
            .Produces<ParsedFiscalDocument>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        IFormFile file,
        IFiscalDocumentReader reader,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return Result.Failure<ParsedFiscalDocument>(Error.Validation("File.Empty", "File is empty.")).ToOk();
        if (file.Length > MaxFileSizeBytes)
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation("File.TooLarge", $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.")).ToOk();

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return Result.Failure<ParsedFiscalDocument>(validation.Error).ToOk();

        ParsedFiscalDocument? parsed;
        try
        {
            parsed = await reader.ReadAsync(file.FileName, stream, ct);
        }
        catch
        {
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation("FiscalDocument.ParseFailed", "Could not parse the fiscal document.")).ToOk();
        }

        if (parsed is null)
            return Result.Failure<ParsedFiscalDocument>(
                Error.Validation("FiscalDocument.UnsupportedFormat", "Unsupported fiscal document format.")).ToOk();

        // Suggest a vendor by issuer document; the form fills VendorId only when still empty.
        if (parsed.IssuerDocument is { Length: > 0 } issuerDocument)
        {
            var vendor = await uow.ReadSet<VendorEntity>()
                .FirstOrDefaultAsync(v => v.CompanyId == companyId.Value && v.Document == issuerDocument, ct);
            if (vendor is not null)
                parsed = parsed with { MatchedVendorId = new VendorId(vendor.Id), MatchedVendorName = vendor.Name };
        }

        return Result.Success(parsed).ToOk();
    }
}
