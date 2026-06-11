using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Authentication;
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
        UserContext user, [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var today = ReceivableClock.Today(clock);

        if (req.Amount <= 0)
            return Results.Problem(detail: "Amount must be greater than zero.", statusCode: 400, title: "Receipt.AmountInvalid");
        if (req.Interest < 0 || req.Fine < 0 || req.Discount < 0)
            return Results.Problem(detail: "Interest, fine and discount cannot be negative.", statusCode: 400, title: "Receipt.AdjustmentInvalid");
        if (req.Discount > req.Amount)
            return Results.Problem(detail: "Discount cannot exceed the amount.", statusCode: 400, title: "Receipt.DiscountInvalid");
        if (req.ReceiptDate > today)
            return Results.Problem(detail: "Receipt date cannot be in the future.", statusCode: 400, title: "Receipt.DateInFuture");

        var receivable = await uow.Set<Receivable>()
            .Include(p => p.Installments) // Receipts auto-include
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (receivable is null) return Results.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");
        if (!receivable.IsReceivable) return Results.Problem(detail: "Only an awaiting-receipt or received receivable can be received.", statusCode: 400, title: "Receivable.NotReceivable");
        if (receivable.IssueDate is { } issue && req.ReceiptDate < issue)
            return Results.Problem(detail: "Receipt date cannot be before the issue date.", statusCode: 400, title: "Receipt.DateBeforeIssue");

        var installment = receivable.FindInstallment(installmentId.Value);
        if (installment is null) return Results.Problem(detail: "Installment not found.", statusCode: 404, title: "Receivable.InstallmentNotFound");

        var bankOk = await uow.ReadSet<BankAccount>().AnyAsync(b => b.Id == req.BankAccountId.Value && b.CompanyId == receivable.CompanyId, ct);
        if (!bankOk) return Results.Problem(detail: "Bank account is invalid for this company.", statusCode: 400, title: "Receipt.BankAccountInvalid");

        // A fully-received installment is reported as settled (409) rather than over-balance (400).
        if (installment.Status == ReceivableInstallmentStatus.Received)
            return Results.Problem(detail: "Installment is already settled.", statusCode: 409, title: "Receipt.InstallmentSettled");

        var principalApplied = req.Amount - req.Interest - req.Fine + req.Discount;
        if (principalApplied <= 0)
            return Results.Problem(detail: "The principal applied must be greater than zero.", statusCode: 400, title: "Receipt.PrincipalInvalid");
        if (installment.ReceivedAmount + principalApplied > installment.Amount)
            return Results.Problem(detail: "Receipt would exceed the installment outstanding balance.", statusCode: 400, title: "Receipt.WouldExceedBalance");

        var receipt = new Receipt(installment.Id, receivable.Id, req.ReceiptDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes, Guid.Parse(user.UserId), clock.GetUtcNow());

        try { installment.RegisterReceipt(receipt); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receipt.InstallmentSettled"); }
        receivable.RecomputeSettlement();

        await uow.AddAsync(receipt, ct); // explicit: grandchild not auto-inserted on aggregate update
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/receivables/{id}", ReceivableResponse.From(receivable, today));
    }
}
