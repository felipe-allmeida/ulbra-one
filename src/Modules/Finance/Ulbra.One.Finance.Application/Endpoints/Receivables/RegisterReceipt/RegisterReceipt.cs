using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
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
        DateOnly ReceiptDate, decimal Amount, BankAccountId BankAccountId, PaymentMethod Method,
        decimal Interest = 0, decimal Fine = 0, decimal Discount = 0, string? Notes = null);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/receipts", Handle)
            .WithName("RegisterReceipt").WithTags("Finance").RequireAuthorization()
            .Produces<ReceivableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, ReceivableInstallmentId installmentId, RegisterReceiptRequest req,
        UserContext user, IUnitOfWork uow, TimeProvider timeProvider, CancellationToken ct)
    {
        var today = ReceivableClock.Today(timeProvider);

        if (req.Amount <= 0)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.AmountInvalid", "Amount must be greater than zero.")).ToOk();
        if (req.Interest < 0 || req.Fine < 0 || req.Discount < 0)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.AdjustmentInvalid", "Interest, fine and discount cannot be negative.")).ToOk();
        if (req.Discount > req.Amount)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.DiscountInvalid", "Discount cannot exceed the amount.")).ToOk();
        if (req.ReceiptDate > today)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.DateInFuture", "Receipt date cannot be in the future.")).ToOk();

        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound("Receivable.NotFound", "Receivable not found.")).ToOk();
        if (!receivable.IsReceivable)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receivable.NotReceivable", "Only an awaiting-receipt or received receivable can be received.")).ToOk();
        if (receivable.IssueDate is { } issue && req.ReceiptDate < issue)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.DateBeforeIssue", "Receipt date cannot be before the issue date.")).ToOk();

        var installment = receivable.FindInstallment(installmentId.Value);
        if (installment is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound("Receivable.InstallmentNotFound", "Installment not found.")).ToOk();

        var bankOk = await uow.ReadSet<BankAccount>().AnyAsync(b => b.Id == req.BankAccountId.Value && b.CompanyId == receivable.CompanyId, ct);
        if (!bankOk)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.BankAccountInvalid", "Bank account is invalid for this company.")).ToOk();

        // A fully-received installment is reported as settled (409) rather than over-balance (400).
        if (installment.Status == ReceivableInstallmentStatus.Received)
            return Result.Failure<ReceivableResponse>(Error.Conflict("Receipt.InstallmentSettled", "Installment is already settled.")).ToOk();

        var principalApplied = req.Amount - req.Interest - req.Fine + req.Discount;
        if (principalApplied <= 0)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.PrincipalInvalid", "The principal applied must be greater than zero.")).ToOk();
        if (installment.ReceivedAmount + principalApplied > installment.Amount)
            return Result.Failure<ReceivableResponse>(Error.Validation("Receipt.WouldExceedBalance", "Receipt would exceed the installment outstanding balance.")).ToOk();

        var receipt = new Receipt(installment.Id, receivable.Id, req.ReceiptDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes, Guid.Parse(user.UserId), timeProvider.GetUtcNow());

        try
        {
            installment.RegisterReceipt(receipt);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receipt.InstallmentSettled");
        }
        receivable.RecomputeSettlement();

        await uow.AddAsync(receipt, ct); // explicit: grandchild not auto-inserted on aggregate update
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{id}", ReceivableResponse.From(receivable, today));
    }
}
