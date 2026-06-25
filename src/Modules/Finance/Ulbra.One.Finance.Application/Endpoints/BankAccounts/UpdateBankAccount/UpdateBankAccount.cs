using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.UpdateBankAccount;

public sealed class UpdateBankAccount : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string BankCode { get; set; } = string.Empty;
        [NormalizeText] public string BankName { get; set; } = string.Empty;
        [NormalizeText] public string Agency { get; set; } = string.Empty;
        [NormalizeText] public string AccountNumber { get; set; } = string.Empty;
        [NormalizeText] public string AccountDigit { get; set; } = string.Empty;
        public BankAccountType AccountType { get; set; }
        [NormalizeText] public string Alias { get; set; } = string.Empty;
        public decimal? OpeningBalance { get; set; }
        public bool Active { get; set; } = true;
        public bool IsDefaultForPayments { get; set; }
        public PaymentMethod? DefaultPaymentMethod { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateBankAccount").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<BankAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(BankAccountId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var acc = await uow.Set<BankAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (acc is null) return TypedResults.Problem(detail: "Bank account not found.", statusCode: 404, title: "BankAccount.NotFound");
        if (string.IsNullOrWhiteSpace(req.Alias))
            return TypedResults.Problem(detail: "Alias is required.", statusCode: 400, title: "BankAccount.NameRequired");
        if (string.IsNullOrWhiteSpace(req.BankCode) || string.IsNullOrWhiteSpace(req.Agency) || string.IsNullOrWhiteSpace(req.AccountNumber))
            return TypedResults.Problem(detail: "Bank code, agency and account number are required.", statusCode: 400, title: "BankAccount.Invalid");

        var companyId = acc.CompanyId;
        var duplicate = await uow.ReadSet<BankAccount>().AnyAsync(a =>
            a.Id != id.Value && a.CompanyId == companyId && a.BankCode == req.BankCode && a.Agency == req.Agency &&
            a.AccountNumber == req.AccountNumber && a.AccountDigit == req.AccountDigit, ct);
        if (duplicate)
            return TypedResults.Problem(detail: "A bank account with these details already exists.", statusCode: 400, title: "BankAccount.Duplicate");

        acc.Update(req.BankCode, req.BankName, req.Agency, req.AccountNumber, req.AccountDigit, req.AccountType, req.Alias, req.OpeningBalance);
        if (req.Active) acc.Activate(); else acc.Deactivate();

        if (req.IsDefaultForPayments)
        {
            // Clear any existing default for this company first (DB unique index is the backstop).
            var existingDefaults = await uow.Set<BankAccount>()
                .Where(b => b.CompanyId == companyId && b.IsDefaultForPayments && b.Id != id.Value)
                .ToListAsync(ct);
            foreach (var other in existingDefaults)
                other.ClearDefaultPayer();
            acc.SetAsDefaultPayer(req.DefaultPaymentMethod ?? PaymentMethod.Pix);
        }
        else
        {
            acc.ClearDefaultPayer();
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BankAccountResponse.From(acc));
    }
}
