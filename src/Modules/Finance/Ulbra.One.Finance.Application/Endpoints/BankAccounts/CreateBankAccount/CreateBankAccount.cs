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

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.CreateBankAccount;

public sealed class CreateBankAccount : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        [NormalizeText] public string BankCode { get; set; } = string.Empty;
        [NormalizeText] public string BankName { get; set; } = string.Empty;
        [NormalizeText] public string Agency { get; set; } = string.Empty;
        [NormalizeText] public string AccountNumber { get; set; } = string.Empty;
        [NormalizeText] public string AccountDigit { get; set; } = string.Empty;
        public BankAccountType AccountType { get; set; }
        [NormalizeText] public string Alias { get; set; } = string.Empty;
        public decimal? OpeningBalance { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateBankAccount").WithTags("Finance")
            .RequireAuthorization().WithRequestNormalizer<Request>()
            .Produces<BankAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Alias))
            return TypedResults.Problem(detail: "Alias is required.", statusCode: 400, title: "BankAccount.NameRequired");
        if (string.IsNullOrWhiteSpace(req.BankCode) || string.IsNullOrWhiteSpace(req.Agency) || string.IsNullOrWhiteSpace(req.AccountNumber))
            return TypedResults.Problem(detail: "Bank code, agency and account number are required.", statusCode: 400, title: "BankAccount.Invalid");

        var companyId = req.CompanyId.Value;
        var duplicate = await uow.ReadSet<BankAccount>().AnyAsync(a =>
            a.CompanyId == companyId && a.BankCode == req.BankCode && a.Agency == req.Agency &&
            a.AccountNumber == req.AccountNumber && a.AccountDigit == req.AccountDigit, ct);
        if (duplicate)
            return TypedResults.Problem(detail: "A bank account with these details already exists.", statusCode: 400, title: "BankAccount.Duplicate");

        var acc = new BankAccount(companyId, req.BankCode, req.BankName, req.Agency, req.AccountNumber,
            req.AccountDigit, req.AccountType, req.Alias, req.OpeningBalance);
        await uow.AddAsync(acc, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/bank-accounts/{new BankAccountId(acc.Id)}", BankAccountResponse.From(acc));
    }
}
