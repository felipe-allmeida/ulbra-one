using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Storage;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ParseBoleto;

/// <summary>Stateless preview: reads a stored boleto blob by key and extracts the linha digitável + dates/amount
/// (AI vision) to pre-fill one installment. Persists nothing; the linha is null when AI is off or fails.</summary>
public sealed class ParseBoleto : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/fiscal-document/parse-boleto", Handle)
            .WithName("ParseBoleto").WithTags("Budgeting")
            .WithSummary("Parses a boleto document without saving it.")
            .WithDescription("Reads a boleto blob already in storage by Key (uploaded via the upload-url step), "
                + "extracts the linha digitável (47 bancário / 48 convênio) plus vencimento/emissão/valor via the "
                + "AI vision layer, and returns them to pre-fill an installment. Persists nothing and mutates no "
                + "requisition. When AI is off or fails, the linha comes back null and the form stays manual.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<ParsedBoleto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company the boleto belongs to. Enforced twice: the caller must have access to it, and "
            + "the storage key must sit under that company's prefix — a key from another company is refused "
            + "with 403/404 rather than parsed.")]
        CompanyId companyId,
        ParseBoletoRequest req,
        UserContext user,
        IFileStorageService storage,
        IBoletoExtractor extractor,
        CancellationToken ct)
    {
        if (!user.CanAccess(companyId.Value))
            return Result.Failure<ParsedBoleto>(
                Error.Forbidden(ErrorCodes.Requisition.Forbidden, "No access to this company.")).ToOk();

        if (!ScopedBlobKey.BelongsTo(req.Key, RequisitionBoletoBlob.KeyRoot, companyId.Value))
            return Result.Failure<ParsedBoleto>(
                Error.NotFound(ErrorCodes.FiscalDocument.NotFound, "Uploaded file not found.")).ToOk();

        await using var stream = await storage.GetAsync(req.Key, ct);
        if (stream is null)
            return Result.Failure<ParsedBoleto>(
                Error.Validation(ErrorCodes.FiscalDocument.NotFound, "Uploaded file not found.")).ToOk();

        using var capped = await FileSizeCap.ReadCappedAsync(stream, ct);
        if (capped is null)
            return Result.Failure<ParsedBoleto>(Error.Validation(ErrorCodes.FiscalDocument.TooLarge,
                $"File exceeds the {FileSizeCap.MaxBytes / (1024 * 1024)}MB limit.")).ToOk();

        var parsed = await extractor.ExtractAsync(req.FileName, capped.ToArray(), ct);
        return Result.Success(parsed).ToOk();
    }
}

public sealed record ParseBoletoRequest(
    [property: SwaggerSchema("Storage key returned by the boleto upload-url request, pointing at the "
        + "already-uploaded blob.")]
    string Key,
    [property: SwaggerSchema("Original file name, used to detect the media type (PDF or image).")]
    [property: SwaggerExample("boleto-08-2026.pdf")]
    string FileName);
