using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A company bank account. Beyond identifying the account, it carries the CNAB profile — "
    + "agreement code, address, payer identity — that a remittance file needs in its header. Missing "
    + "pieces of that profile are a common reason an installment cannot be remitted.")]
public sealed record BankAccountResponse(
    BankAccountId Id,
    [property: SwaggerSchema("Company that owns the account.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Bank code (COMPE), three digits.")]
    [property: SwaggerExample("341")]
    string BankCode,
    [property: SwaggerSchema("Bank name.")]
    [property: SwaggerExample("Itaú Unibanco")]
    string BankName,
    [property: SwaggerSchema("Branch number (agência), without the check digit.")]
    [property: SwaggerExample("1234")]
    string Agency,
    [property: SwaggerSchema("Account number, without the check digit.")]
    [property: SwaggerExample("56789")]
    string AccountNumber,
    [property: SwaggerSchema("Account check digit, kept apart because the CNAB layout carries the two in "
        + "separate fields.")]
    [property: SwaggerExample("0")]
    string AccountDigit,
    [property: SwaggerSchema("Kind of account.")]
    BankAccountType AccountType,
    [property: SwaggerSchema("Friendly name shown in the UI, so people do not have to recognise account "
        + "numbers.")]
    [property: SwaggerExample("Itaú — conta movimento")]
    string Alias,
    [property: SwaggerSchema("Balance the account started from, in BRL. It is the anchor the cash-flow "
        + "projection builds on; null means none was informed.")]
    [property: SwaggerExample("125000.00")]
    decimal? OpeningBalance,
    [property: SwaggerSchema("Whether the account is in use.")]
    bool Active,
    [property: SwaggerSchema("Whether this is the account payments go out of by default. It is the one "
        + "used to decide whether an installment is remittance-eligible, so changing it changes what the "
        + "payment queue offers.")]
    bool IsDefaultForPayments,
    [property: SwaggerSchema("Agreement code (código do convênio) the bank assigned for CNAB. Without it "
        + "no remittance can be generated from this account.")]
    [property: SwaggerExample("1234567")]
    string? CnabAgreementCode,
    [property: SwaggerSchema("Ledger account this bank account posts to.")]
    LedgerAccountId? LedgerAccountId,
    [property: SwaggerSchema("Account identifier as it appears in OFX statements — how an imported "
        + "statement is matched to this account.")]
    [property: SwaggerExample("56789-0")]
    string? OfxAccountId,
    // CNAB remittance-header profile (agency check digit + paying-company address) — echoed for edit pre-fill.
    [property: SwaggerSchema("Branch check digit, required by the CNAB header.")]
    [property: SwaggerExample("7")]
    string AgencyDigit,
    [property: SwaggerSchema("Paying company street, for the CNAB header. Empty when never filled.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string AddressStreet,
    [property: SwaggerSchema("Paying company street number, for the CNAB header.")]
    [property: SwaggerExample("8001")]
    string AddressNumber,
    [property: SwaggerSchema("Paying company address complement, for the CNAB header.")]
    string AddressComplement,
    [property: SwaggerSchema("Paying company city, for the CNAB header.")]
    [property: SwaggerExample("Canoas")]
    string AddressCity,
    [property: SwaggerSchema("Paying company CEP, digits only, for the CNAB header.")]
    [property: SwaggerExample("92425900")]
    string AddressCep,
    [property: SwaggerSchema("Paying company state (UF), for the CNAB header.")]
    [property: SwaggerExample("RS")]
    string AddressState,
    // CNAB payer identity override (empty = use the owning company's CNPJ/legal name).
    [property: SwaggerSchema("CNPJ to declare as payer in the CNAB file, when it must differ from the "
        + "owning company's. EMPTY means no override — the company's own CNPJ is used, not that the field "
        + "is missing.")]
    [property: SwaggerExample("88332580000165")]
    string PayerCnpj,
    [property: SwaggerSchema("Legal name to declare as payer in the CNAB file. Empty means no override, "
        + "same rule as PayerCnpj.")]
    [property: SwaggerExample("Universidade Luterana do Brasil")]
    string PayerLegalName)
{
    public static BankAccountResponse From(BankAccount a) => new(
        new BankAccountId(a.Id), new CompanyId(a.CompanyId), a.BankCode, a.BankName, a.Agency,
        a.AccountNumber, a.AccountDigit, a.AccountType, a.Alias, a.OpeningBalance, a.Active,
        a.IsDefaultForPayments, a.CnabAgreementCode,
        a.LedgerAccountId is { } lid ? new LedgerAccountId(lid) : null, a.OfxAccountId,
        a.AgencyDigit, a.AddressStreet, a.AddressNumber, a.AddressComplement,
        a.AddressCity, a.AddressCep, a.AddressState,
        a.PayerCnpj, a.PayerLegalName);
}
