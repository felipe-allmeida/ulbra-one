using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record BankAccountResponse(
    BankAccountId Id, CompanyId CompanyId, string BankCode, string BankName, string Agency,
    string AccountNumber, string AccountDigit, BankAccountType AccountType, string Alias,
    decimal? OpeningBalance, bool Active,
    bool IsDefaultForPayments, PaymentMethod? DefaultPaymentMethod)
{
    public static BankAccountResponse From(BankAccount a) => new(
        new BankAccountId(a.Id), new CompanyId(a.CompanyId), a.BankCode, a.BankName, a.Agency,
        a.AccountNumber, a.AccountDigit, a.AccountType, a.Alias, a.OpeningBalance, a.Active,
        a.IsDefaultForPayments, a.DefaultPaymentMethod);
}
