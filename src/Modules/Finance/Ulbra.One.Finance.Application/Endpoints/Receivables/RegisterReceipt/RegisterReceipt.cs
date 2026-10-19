using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.RegisterReceipt;

public sealed class RegisterReceipt : IEndpoint
{
    public sealed record RegisterReceiptRequest(
        [property: SwaggerSchema("When the money actually landed.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly ReceiptDate,
        [property: SwaggerSchema("Principal received, in BRL - the part that reduces the installment "
            + "balance. Interest, fine and discount are recorded apart and do NOT count toward it. "
            + "Receiving beyond the outstanding balance is rejected.")]
        [property: SwaggerExample("1250.00")]
        decimal Amount,
        [property: SwaggerSchema("Bank account the money landed in.")]
        BankAccountId BankAccountId,
        [property: SwaggerSchema("How it was received.")]
        PaymentMethod Method,
        [property: SwaggerSchema("Interest collected on top of the principal, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Interest = 0,
        [property: SwaggerSchema("Late fee collected on top of the principal, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Fine = 0,
        [property: SwaggerSchema("Discount granted, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Discount = 0,
        [property: SwaggerSchema("Free-text note kept on the receipt record.")]
        string? Notes = null);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/receipts", Handle)
            .WithName("RegisterReceipt").WithTags("Finance")
            .WithSummary("Registers a receipt against an installment.")
            .WithDescription("Settles part or all of one installment on the receivable side. Receipts are APPEND-ONLY, "
                + "mirroring payments: once a receivable has one it can no longer be edited or cancelled, and "
                + "undoing means reversing. Amount is the principal and cannot exceed the outstanding balance; "
                + "interest, fine and discount are recorded separately.").RequirePermission(Permissions.ReceivablesReceive)
            .Produces<ReceivableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, ReceivableInstallmentId installmentId, RegisterReceiptRequest req,
        UserContext user, IUnitOfWork uow, TimeProvider timeProvider, CancellationToken ct)
    {
        var today = ReceivableClock.Today(timeProvider);

        if (req.Amount <= 0)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.AmountInvalid, "Amount must be greater than zero.")).ToOk();
        if (req.Interest < 0 || req.Fine < 0 || req.Discount < 0)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.AdjustmentInvalid, "Interest, fine and discount cannot be negative.")).ToOk();
        if (req.Discount > req.Amount)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.DiscountInvalid, "Discount cannot exceed the amount.")).ToOk();
        if (req.ReceiptDate > today)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.DateInFuture, "Receipt date cannot be in the future.")).ToOk();

        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound(ErrorCodes.Receivable.NotFound, "Receivable not found.")).ToOk();
        if (!receivable.IsReceivable)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receivable.NotReceivable, "Only an awaiting-receipt or received receivable can be received.")).ToOk();
        if (receivable.IssueDate is { } issue && req.ReceiptDate < issue)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.DateBeforeIssue, "Receipt date cannot be before the issue date.")).ToOk();

        var installment = receivable.FindInstallment(installmentId.Value);
        if (installment is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound(ErrorCodes.Receivable.InstallmentNotFound, "Installment not found.")).ToOk();

        var bankOk = await uow.ReadSet<BankAccount>().AnyAsync(b => b.Id == req.BankAccountId.Value && b.CompanyId == receivable.CompanyId, ct);
        if (!bankOk)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.BankAccountInvalid, "Bank account is invalid for this company.")).ToOk();

        // A fully-received installment is reported as settled (409) rather than over-balance (400).
        if (installment.Status == ReceivableInstallmentStatus.Received)
            return Result.Failure<ReceivableResponse>(Error.Conflict(ErrorCodes.Receipt.InstallmentSettled, "Installment is already settled.")).ToOk();

        var principalApplied = req.Amount - req.Interest - req.Fine + req.Discount;
        if (principalApplied <= 0)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.PrincipalInvalid, "The principal applied must be greater than zero.")).ToOk();
        if (installment.ReceivedAmount + principalApplied > installment.Amount)
            return Result.Failure<ReceivableResponse>(Error.Validation(ErrorCodes.Receipt.WouldExceedBalance, "Receipt would exceed the installment outstanding balance.")).ToOk();

        var receipt = new Receipt(installment.Id, receivable.Id, req.ReceiptDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes, Guid.Parse(user.UserId), timeProvider.GetUtcNow());

        try
        {
            installment.RegisterReceipt(receipt);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResults.InvalidState(ex.Message, ErrorCodes.Receipt.InstallmentSettled);
        }
        receivable.RecomputeSettlement();

        await uow.AddAsync(receipt, ct); // explicit: grandchild not auto-inserted on aggregate update
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{id}", ReceivableResponse.From(receivable, today));
    }
}
