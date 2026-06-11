using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.DownloadReceiptProof;

public sealed class DownloadReceiptProof : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/receipts/{receiptId}/proof", Handle)
            .WithName("DownloadReceiptProof").WithTags("Finance")
            .RequireAuthorization();

    private static async Task<IResult> Handle(
        ReceivableId id,
        ReceiptId receiptId,
        IFileStorageService storage,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var receivable = await uow.ReadSet<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        var receipt = receivable?.FindReceipt(receiptId.Value);
        if (receipt?.ProofStoragePath is null) return Results.NotFound();

        var stream = await storage.GetAsync(receipt.ProofStoragePath, ct);
        if (stream is null) return Results.NotFound();

        return Results.File(stream, receipt.ProofContentType!, receipt.ProofFileName!);
    }
}
