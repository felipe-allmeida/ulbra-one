using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.DeleteAttachment;

public sealed class DeleteAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}/attachments/{attachmentId}", Handle)
            .WithName("DeletePayableAttachment").WithTags("Finance")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        PayableAttachmentId attachmentId,
        IFileStorageService storage,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        ILogger<DeleteAttachment> logger,
        CancellationToken ct)
    {
        var a = await uow.Set<PayableAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.PayableId == id.Value, ct);
        if (a is null) return Results.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        uow.Remove(a);
        await uow.SaveChangesAsync(ct);

        try { await storage.DeleteAsync(a.StoragePath, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to remove blob {Path}", a.StoragePath); }

        return Results.NoContent();
    }
}
