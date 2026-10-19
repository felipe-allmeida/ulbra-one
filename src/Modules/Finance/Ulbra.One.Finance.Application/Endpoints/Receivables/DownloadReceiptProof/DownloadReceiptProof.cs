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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.DownloadReceiptProof;

public sealed class DownloadReceiptProof : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/receipts/{receiptId}/proof", Handle)
            .WithName("DownloadReceiptProof").WithTags("Finance")
            .WithSummary("Downloads a receipt proof.")
            .WithDescription("Streams the document attached to a receipt back with its original name and content type.")
            .RequirePermission(Permissions.ReceivablesRead);

    private static async Task<IResult> Handle(
        ReceivableId id,
        ReceiptId receiptId,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var receivable = await uow.ReadSet<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        // Scope by company (Receivable has no unit); denied returns the same 404. See AEL-764.
        if (receivable is null || !user.CanAccess(receivable.CompanyId)) return Results.NotFound();

        var receipt = receivable.FindReceipt(receiptId.Value);
        if (receipt?.ProofStoragePath is null) return Results.NotFound();

        var stream = await storage.GetAsync(receipt.ProofStoragePath, ct);
        if (stream is null) return Results.NotFound();

        return Results.File(stream, receipt.ProofContentType!, receipt.ProofFileName!);
    }
}
