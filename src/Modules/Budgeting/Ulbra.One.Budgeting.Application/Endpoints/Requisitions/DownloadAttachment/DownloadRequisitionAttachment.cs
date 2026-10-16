using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DownloadAttachment;

public sealed class DownloadRequisitionAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DownloadRequisitionAttachment").WithTags("Budgeting")
            .WithSummary("Downloads a requisition attachment.")
            .WithDescription("Streams the stored file back with its original name and content type. "
                + "Returns 404 when either the requisition or the attachment does not exist.")
            .RequirePermission(Permissions.RequisitionsRead);

    private static async Task<IResult> Handle(
        RequisitionId id,
        RequisitionAttachmentId attachmentId,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var a = await uow.ReadSet<RequisitionAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.RequisitionId == id.Value, ct);
        if (a is null) return TypedResults.NotFound();

        // Scope by the parent requisition's company; denied returns the same 404 (doesn't leak existence). See AEL-764.
        var companyId = await uow.ReadSet<Requisition>()
            .Where(r => r.Id == id.Value).Select(r => r.CompanyId).FirstOrDefaultAsync(ct);
        if (!user.CanAccess(companyId)) return TypedResults.NotFound();

        var stream = await storage.GetAsync(a.StoragePath, ct);
        if (stream is null) return TypedResults.NotFound();

        return TypedResults.File(stream, a.ContentType, a.FileName);
    }
}
