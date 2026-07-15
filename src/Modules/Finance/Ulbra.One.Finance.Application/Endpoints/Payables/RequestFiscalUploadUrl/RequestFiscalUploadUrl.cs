using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain;

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
            .RequirePermission(Permissions.PayablesWrite)
            .Produces<UploadTarget>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        RequestFiscalUploadUrlRequest req,
        IFileStorageService storage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
            return Result.Failure<UploadTarget>(Error.Validation("FiscalDocument.FileNameRequired", "FileName is required.")).ToOk();

        UploadTarget target;
        try
        {
            target = await storage.CreateUploadUrl(req.FileName, TimeSpan.FromMinutes(15), ct);
        }
        catch (NotSupportedException)
        {
            return Result.Failure<UploadTarget>(
                Error.Validation("Storage.PresignNotSupported",
                    "Presigned upload is not supported by the current storage provider.")).ToOk();
        }

        return Result.Success(target).ToOk();
    }
}

public sealed record RequestFiscalUploadUrlRequest(string FileName);
