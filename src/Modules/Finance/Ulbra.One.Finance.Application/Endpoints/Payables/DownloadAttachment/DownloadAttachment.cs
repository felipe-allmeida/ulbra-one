using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.DownloadAttachment;

public sealed class DownloadAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DownloadPayableAttachment").WithTags("Finance")
            .RequireAuthorization();

    private static async Task<IResult> Handle(
        PayableId id,
        PayableAttachmentId attachmentId,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var a = await uow.ReadSet<PayableAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.PayableId == id.Value, ct);
        if (a is null) return TypedResults.NotFound();

        var stream = await storage.GetAsync(a.StoragePath, ct);
        if (stream is null) return TypedResults.NotFound();

        return TypedResults.File(stream, a.ContentType, a.FileName);
    }
}
