using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.DownloadAttachment;

public sealed class DownloadAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DownloadPayableAttachment").WithTags("Finance")
            .WithSummary("Downloads a payable attachment.")
            .WithDescription("Streams the stored file back with its original name and content type.")
            .RequirePermission(Permissions.PayablesRead);

    private static async Task<IResult> Handle(
        PayableId id,
        PayableAttachmentId attachmentId,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var a = await uow.ReadSet<PayableAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.PayableId == id.Value, ct);
        if (a is null) return TypedResults.NotFound();

        // Per-resource authorization: the attachment carries no company; scope by the parent payable's company.
        // A denied scope returns the same 404 as a missing attachment (doesn't leak existence). See AEL-764.
        var companyId = await uow.ReadSet<Payable>()
            .Where(p => p.Id == id.Value).Select(p => p.CompanyId).FirstOrDefaultAsync(ct);
        if (!user.CanAccess(companyId)) return TypedResults.NotFound();

        var stream = await storage.GetAsync(a.StoragePath, ct);
        if (stream is null) return TypedResults.NotFound();

        return TypedResults.File(stream, a.ContentType, a.FileName);
    }
}
