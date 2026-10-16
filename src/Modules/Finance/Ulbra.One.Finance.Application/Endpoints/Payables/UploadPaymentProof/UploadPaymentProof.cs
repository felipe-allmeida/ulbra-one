using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadPaymentProof;

public sealed class UploadPaymentProof : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/payments/{paymentId}/proof", Handle)
            .WithName("UploadPaymentProof").WithTags("Finance")
            .WithSummary("Attaches the proof of a payment.")
            .WithDescription("Accepts multipart/form-data with the payment receipt (comprovante), up to 25 MB, bound to "
                + "one registered payment. Re-uploading replaces the previous proof.")
            .RequirePermission(Permissions.PayablesPay).DisableAntiforgery()
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        PaymentId paymentId,
        IFormFile file,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null || !user.CanAccess(payable.CompanyId))
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var payment = payable.FindPayment(paymentId.Value);
        if (payment is null)
            return ErrorResults.NotFound("Payment not found.", ErrorCodes.Payment.NotFound);

        if (file.Length == 0)
            return ErrorResults.InvalidRequest("File is empty.", ErrorCodes.File.Empty);
        if (file.Length > MaxFileSizeBytes)
            return ErrorResults.InvalidRequest($"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", ErrorCodes.File.TooLarge);

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return ErrorResults.InvalidRequest(validation.Error.Description, ErrorCodes.File.UnsupportedType);

        var key = await storage.SaveAsync(file.FileName, stream, ct);
        payment.SetProof(file.FileName, file.ContentType, key, file.Length);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/payments/{paymentId}/proof",
            PaymentResponse.From(payment));
    }
}
