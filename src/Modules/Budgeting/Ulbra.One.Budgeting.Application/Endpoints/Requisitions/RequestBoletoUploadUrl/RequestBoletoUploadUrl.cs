using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Storage;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.RequestBoletoUploadUrl;

/// <summary>Returns a presigned PUT URL + storage key so the client uploads the boleto directly (AEL-938).
/// Requires a presign-capable provider; the local-dev disk provider answers 400 <c>Storage.PresignNotSupported</c>.</summary>
public sealed class RequestBoletoUploadUrl : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/fiscal-document/upload-url", Handle)
            .WithName("RequestBoletoUploadUrl").WithTags("Budgeting")
            .WithSummary("Requests an upload URL for a boleto document.")
            .WithDescription("First step of the boleto reader: returns a presigned PUT URL plus the storage Key, "
                + "scoped to the company. The client PUTs the file straight to storage, then calls parse-boleto "
                + "with that Key. Requires a presign-capable storage provider; the local-dev disk provider "
                + "answers 400 Storage.PresignNotSupported.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<UploadTarget>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company the boleto belongs to. Required: it scopes the storage key the presigned URL "
            + "points at, and the caller must have access to it.")]
        CompanyId companyId,
        RequestBoletoUploadUrlRequest req,
        UserContext user,
        IFileStorageService storage,
        CancellationToken ct)
    {
        if (!user.CanAccess(companyId.Value))
            return Result.Failure<UploadTarget>(
                Error.Forbidden(ErrorCodes.Requisition.Forbidden, "No access to this company.")).ToOk();

        if (string.IsNullOrWhiteSpace(req.FileName))
            return Result.Failure<UploadTarget>(
                Error.Validation(ErrorCodes.FiscalDocument.FileNameRequired, "FileName is required.")).ToOk();

        UploadTarget target;
        try
        {
            target = await storage.CreateUploadUrl(
                req.FileName, TimeSpan.FromMinutes(15), ScopedBlobKey.PrefixFor(RequisitionBoletoBlob.KeyRoot, companyId.Value), ct);
        }
        catch (NotSupportedException)
        {
            return Result.Failure<UploadTarget>(
                Error.Validation(ErrorCodes.Storage.PresignNotSupported,
                    "Presigned upload is not supported by the current storage provider.")).ToOk();
        }

        return Result.Success(target).ToOk();
    }
}

public sealed record RequestBoletoUploadUrlRequest(
    [property: SwaggerSchema("Name of the boleto file to upload. Its extension decides the storage key and "
        + "the content type of the presigned URL.")]
    [property: SwaggerExample("boleto-08-2026.pdf")]
    string FileName);
