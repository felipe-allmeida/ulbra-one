using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.UploadReceiptProof;

public sealed class UploadReceiptProof : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/receipts/{receiptId}/proof", Handle)
            .WithName("UploadReceiptProof").WithTags("Finance")
            .RequireAuthorization().DisableAntiforgery()
            .Produces<ReceiptResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        ReceivableId id,
        ReceiptId receiptId,
        IFormFile file,
        IFileStorageService storage,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null)
            return Results.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");

        var receipt = receivable.FindReceipt(receiptId.Value);
        if (receipt is null)
            return Results.Problem(detail: "Receipt not found.", statusCode: 404, title: "Receipt.NotFound");

        if (file.Length == 0)
            return Results.Problem(detail: "File is empty.", statusCode: 400, title: "File.Empty");
        if (file.Length > MaxFileSizeBytes)
            return Results.Problem(detail: $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", statusCode: 400, title: "File.TooLarge");

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return Results.Problem(detail: validation.Error.Description, statusCode: 400, title: "File.UnsupportedType");

        var key = await storage.SaveAsync(file.FileName, stream, ct);
        receipt.SetProof(file.FileName, file.ContentType, key, file.Length);
        await uow.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/receivables/{id}/receipts/{receiptId}/proof",
            ReceiptResponse.From(receipt));
    }
}
