using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DeleteAttachment;

public sealed class DeleteRequisitionAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DeleteRequisitionAttachment").WithTags("Budgeting")
            .WithSummary("Removes an attachment from a requisition.")
            .WithDescription("Deletes one attachment, identified by its own id within the requisition. "
                + "Returns 404 when either the requisition or the attachment does not exist.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RequisitionId id,
        RequisitionAttachmentId attachmentId,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        ILogger<DeleteRequisitionAttachment> logger,
        CancellationToken ct)
    {
        var a = await uow.Set<RequisitionAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.RequisitionId == id.Value, ct);
        if (a is null) return ErrorResults.NotFound("Attachment not found.", ErrorCodes.Requisition.AttachmentNotFound);

        // Scope by the parent requisition's full Empresa × Unidade × Centro de Custo (AEL-883, not just the
        // company as in AEL-764); denied returns the same 404-hide.
        var dims = await uow.ReadSet<Requisition>()
            .Where(r => r.Id == id.Value)
            .Select(r => new { r.CompanyId, r.UnitId, r.CostCenterId }).FirstOrDefaultAsync(ct);
        if (dims is null || !user.CanAccessScope(dims.CompanyId, dims.UnitId, dims.CostCenterId))
            return ErrorResults.NotFound("Attachment not found.", ErrorCodes.Requisition.AttachmentNotFound);

        uow.Remove(a);
        await uow.SaveChangesAsync(ct);

        try { await storage.DeleteAsync(a.StoragePath, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to remove blob {Path}", a.StoragePath); }

        return TypedResults.NoContent();
    }
}
