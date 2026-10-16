using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.DeleteAttachment;

public sealed class DeleteAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DeletePayableAttachment").WithTags("Finance")
            .WithSummary("Removes an attachment from a payable.")
            .WithDescription("Deletes one attachment by its own id within the payable. Returns 404 when either the payable "
                + "or the attachment does not exist.")
            .RequirePermission(Permissions.PayablesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        PayableAttachmentId attachmentId,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        ILogger<DeleteAttachment> logger,
        CancellationToken ct)
    {
        var a = await uow.Set<PayableAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.PayableId == id.Value, ct);
        if (a is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // Scope by the parent payable's company; denied = same 404 (doesn't leak existence). See AEL-764.
        var companyId = await uow.ReadSet<Payable>()
            .Where(p => p.Id == id.Value).Select(p => p.CompanyId).FirstOrDefaultAsync(ct);
        if (!user.CanAccess(companyId)) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        uow.Remove(a);
        await uow.SaveChangesAsync(ct);

        try { await storage.DeleteAsync(a.StoragePath, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to remove blob {Path}", a.StoragePath); }

        return TypedResults.NoContent();
    }
}
