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

namespace Ulbra.One.Finance.Application.Endpoints.Payables.DownloadPaymentProof;

public sealed class DownloadPaymentProof : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/payments/{paymentId}/proof", Handle)
            .WithName("DownloadPaymentProof").WithTags("Finance")
            .RequirePermission(Permissions.PayablesRead);

    private static async Task<IResult> Handle(
        PayableId id,
        PaymentId paymentId,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        var payment = payable?.FindPayment(paymentId.Value);
        if (payment?.ProofStoragePath is null) return TypedResults.NotFound();

        var stream = await storage.GetAsync(payment.ProofStoragePath, ct);
        if (stream is null) return TypedResults.NotFound();

        return TypedResults.File(stream, payment.ProofContentType!, payment.ProofFileName!);
    }
}
