using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.UploadReceiptProof;

public sealed class UploadReceiptProof : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/receipts/{receiptId}/proof", Handle)
            .WithName("UploadReceiptProof").WithTags("Finance")
            .WithSummary("Attaches the proof of a receipt.")
            .WithDescription("Accepts multipart/form-data with the receipt document, up to 25 MB, bound to one registered "
                + "receipt. Re-uploading replaces the previous proof.")
            .RequirePermission(Permissions.ReceivablesReceive).DisableAntiforgery()
            .Produces<ReceiptResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        ReceivableId id,
        ReceiptId receiptId,
        IFormFile file,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null || !user.CanAccess(receivable.CompanyId))
            return Result.Failure<ReceiptResponse>(Error.NotFound(ErrorCodes.Receivable.NotFound, "Receivable not found.")).ToOk();

        var receipt = receivable.FindReceipt(receiptId.Value);
        if (receipt is null)
            return Result.Failure<ReceiptResponse>(Error.NotFound(ErrorCodes.Receipt.NotFound, "Receipt not found.")).ToOk();

        if (file.Length == 0)
            return Result.Failure<ReceiptResponse>(Error.Validation(ErrorCodes.File.Empty, "File is empty.")).ToOk();
        if (file.Length > MaxFileSizeBytes)
            return Result.Failure<ReceiptResponse>(Error.Validation(ErrorCodes.File.TooLarge, $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.")).ToOk();

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return Result.Failure<ReceiptResponse>(validation.Error).ToOk();

        // Defensive: the validator already restores the position after its magic-byte read, but
        // resetting here keeps SaveAsync correct independent of that internal contract.
        stream.Position = 0;
        var key = await storage.SaveAsync(file.FileName, stream, ct);
        receipt.SetProof(file.FileName, file.ContentType, key, file.Length);
        await uow.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/receivables/{id}/receipts/{receiptId}/proof",
            ReceiptResponse.From(receipt));
    }
}
