using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.FiscalDocuments;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RequestFiscalUploadUrl;

/// <summary>
/// Requests a presigned PUT URL for uploading a fiscal document directly to object storage.
/// The client PUTs the file to <c>UploadUrl</c> and then calls parse or upload with the returned <c>Key</c>.
/// On local-dev storage (no presign support) returns 400 <c>Storage.PresignNotSupported</c> so the frontend
/// can fall back to multipart upload.
/// </summary>
public sealed class RequestFiscalUploadUrl : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/fiscal-document/upload-url", Handle)
            .WithName("RequestFiscalUploadUrl").WithTags("Finance")
            .WithSummary("Requests an upload URL for a fiscal document.")
            .WithDescription("First step of the fiscal-document flow: returns a presigned PUT URL plus the storage Key. "
                + "The client PUTs the file straight to storage, then calls parse or upload with that Key - the "
                + "file never passes through this API. Local-development storage cannot presign and answers 400 "
                + "Storage.PresignNotSupported, which is the signal to fall back to a multipart upload rather "
                + "than an error to show the user.")
            .RequirePermission(Permissions.PayablesWrite)
            .Produces<UploadTarget>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company the document will belong to. Required: it scopes the storage key the "
            + "presigned URL points at, and the caller must have access to it.")]
        CompanyId companyId,
        RequestFiscalUploadUrlRequest req,
        UserContext user,
        IFileStorageService storage,
        CancellationToken ct)
    {
        // The issued key is namespaced by company; only those with access to the company can mint it.
        if (!user.CanAccess(companyId.Value))
            return Result.Failure<UploadTarget>(
                Error.Forbidden(ErrorCodes.FiscalDocument.CompanyForbidden, "No access to this company.")).ToOk();

        if (string.IsNullOrWhiteSpace(req.FileName))
            return Result.Failure<UploadTarget>(Error.Validation(ErrorCodes.FiscalDocument.FileNameRequired, "FileName is required.")).ToOk();

        UploadTarget target;
        try
        {
            target = await storage.CreateUploadUrl(
                req.FileName, TimeSpan.FromMinutes(15), FiscalDocumentKey.PrefixFor(companyId.Value), ct);
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

public sealed record RequestFiscalUploadUrlRequest(
    [property: SwaggerSchema("Name of the file to be uploaded. Its extension is what decides the "
        + "storage key and the content type of the presigned URL.")]
    [property: SwaggerExample("nfse-12345.xml")]
    string FileName);
