using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadPaymentProof;

public sealed class UploadPaymentProof : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/payments/{paymentId}/proof", Handle)
            .WithName("UploadPaymentProof").WithTags("Finance")
            .RequireAuthorization().DisableAntiforgery()
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        PaymentId paymentId,
        IFormFile file,
        IFileStorageService storage,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return Results.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var payment = payable.FindPayment(paymentId.Value);
        if (payment is null)
            return Results.Problem(detail: "Payment not found.", statusCode: 404, title: "Payment.NotFound");

        if (file.Length == 0)
            return Results.Problem(detail: "File is empty.", statusCode: 400, title: "File.Empty");
        if (file.Length > MaxFileSizeBytes)
            return Results.Problem(detail: $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", statusCode: 400, title: "File.TooLarge");

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return Results.Problem(detail: validation.Error.Description, statusCode: 400, title: "File.UnsupportedType");

        var key = await storage.SaveAsync(file.FileName, stream, ct);
        payment.SetProof(file.FileName, file.ContentType, key, file.Length);
        await uow.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/payables/{id}/payments/{paymentId}/proof",
            PaymentResponse.From(payment));
    }
}
