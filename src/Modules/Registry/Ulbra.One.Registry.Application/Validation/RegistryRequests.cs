using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Validation;

public sealed record AddressRequest(
    [property: SwaggerSchema("Postal code. Digits only or formatted — both are accepted.")]
    [property: SwaggerExample("90420-111")]
    string? Cep,
    [property: SwaggerSchema("Street name, without the number.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string? Street,
    [property: SwaggerSchema("Street number. Free text, so \"s/n\" is valid.")]
    [property: SwaggerExample("8001")]
    string? Number,
    [property: SwaggerSchema("Apartment, block or reference note.")]
    [property: SwaggerExample("Prédio 9, sala 210")]
    string? Complement,
    [property: SwaggerSchema("Neighborhood (bairro).")]
    [property: SwaggerExample("São José")]
    string? Neighborhood,
    [property: SwaggerSchema("City name.")]
    [property: SwaggerExample("Canoas")]
    string? City,
    [property: SwaggerSchema("Two-letter state code (UF).")]
    [property: SwaggerExample("RS")]
    string? State);

public sealed record BankInfoRequest(
    [property: SwaggerSchema("Bank code (código de compensação), three digits.")]
    [property: SwaggerExample("033")]
    string? BankCode,
    [property: SwaggerSchema("Branch number (agência), without the check digit.")]
    [property: SwaggerExample("1234")]
    string? Branch,
    [property: SwaggerSchema("Account number, without the check digit — that goes in AccountDigit.")]
    [property: SwaggerExample("56789")]
    string? Account,
    [property: SwaggerSchema("Account check digit.")]
    [property: SwaggerExample("0")]
    string? AccountDigit,
    [property: SwaggerSchema("Pix key of any kind — CPF/CNPJ, e-mail, phone or a random key.")]
    [property: SwaggerExample("financeiro@fornecedor.com.br")]
    string? PixKey);

public sealed record VendorRequest(
    [property: SwaggerSchema("Legal nature of the counterparty. It decides whether Document is read as a "
        + "CNPJ or a CPF.")]
    PersonType PersonType,
    [property: SwaggerSchema("Legal name (razão social). Required.")]
    [property: SwaggerExample("Construtora Farroupilha Ltda")]
    [property: NormalizeText] string Name,
    [property: SwaggerSchema("Trading name (nome fantasia), when it differs from the legal name.")]
    [property: SwaggerExample("Farroupilha Engenharia")]
    [property: NormalizeText] string? TradeName,
    [property: SwaggerSchema("CNPJ or CPF, matching PersonType. Accepted formatted or bare; stored "
        + "normalized (alphanumeric CNPJ, digits CPF) and validated with the check digits.")]
    [property: SwaggerExample("12.345.678/0001-95")]
    [property: NormalizeText] string Document,
    [property: SwaggerSchema("Contact e-mail.")]
    [property: SwaggerExample("financeiro@farroupilha.com.br")]
    string? Email,
    [property: SwaggerSchema("Contact phone, with area code.")]
    [property: SwaggerExample("(51) 3477-9000")]
    string? Phone,
    [property: SwaggerSchema("Postal address.")]
    AddressRequest? Address,
    [property: SwaggerSchema("Agreed payment terms, free text — shown to whoever schedules the payment.")]
    [property: SwaggerExample("30/60/90 dias após o aceite")]
    [property: NormalizeText] string? PaymentTerms,
    [property: SwaggerSchema("Bank details used to pay this vendor.")]
    BankInfoRequest? BankInfo,
    [property: SwaggerSchema("Tax regime. Drives which withholdings apply to the vendor's invoices.")]
    TaxRegime TaxRegime,
    [property: SwaggerSchema("When to settle: on the due date, or on a fixed day of the month set by "
        + "PaymentScheduleDay.")]
    PaymentScheduleMode PaymentScheduleMode = PaymentScheduleMode.OnDueDate,
    [property: SwaggerSchema("Day of the month to pay on. Only meaningful when PaymentScheduleMode pins "
        + "payments to a fixed day.")]
    [property: SwaggerExample("10")]
    int? PaymentScheduleDay = null,
    // Indicativos p/ INSS (cadastro sugere; NF confirma): CPRB previsto (Sim/Não/null) + anexo do Simples.
    [property: SwaggerSchema("Whether CPRB is expected for this vendor, for INSS purposes. The registry "
        + "only suggests it — the fiscal document confirms. Null means unknown.")]
    bool? CprbForeseen = null,
    [property: SwaggerSchema("Simples Nacional annex, for INSS purposes. Suggestion only, same as "
        + "CprbForeseen.")]
    SimplesAnexo? SimplesAnexo = null,
    // Accepted payment methods. Omitted/empty = no restriction.
    [property: SwaggerSchema("Payment methods this vendor accepts. Omitted or empty means no restriction.")]
    IReadOnlyList<PaymentMethod>? AcceptedPaymentMethods = null,
    // Per-tax withholding rates (rate override only). Omitted/empty = use the system default.
    [property: SwaggerSchema("Per-tax withholding rate overrides. Only the rate is overridden — whether a "
        + "tax applies at all still comes from the regime. Omitted or empty uses the system defaults.")]
    IReadOnlyList<VendorWithholdingRate>? WithholdingRates = null,
    // Default expense ledger accounts for this vendor (LedgerAccount ids). Omitted/empty = none.
    [property: SwaggerSchema("Default expense ledger accounts suggested when classifying this vendor's "
        + "payables. Omitted or empty means none.")]
    IReadOnlyList<LedgerAccountId>? AssociatedLedgerAccountIds = null,
    // Primary CNAE from the CNPJ lookup (code + description); optional.
    [property: SwaggerSchema("Primary CNAE code, normally filled from the CNPJ lookup.")]
    [property: SwaggerExample("4120-4/00")]
    [property: NormalizeText] string? PrimaryCnaeCode = null,
    [property: SwaggerSchema("Description of the primary CNAE.")]
    [property: SwaggerExample("Construção de edifícios")]
    [property: NormalizeText] string? PrimaryCnaeDescription = null);

public sealed record CustomerRequest(
    [property: SwaggerSchema("Company the customer is registered under.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Legal nature of the counterparty. It decides whether Document is read as a "
        + "CNPJ or a CPF.")]
    PersonType PersonType,
    [property: SwaggerSchema("Legal name (razão social) or the person's full name. Required.")]
    [property: SwaggerExample("Instituto Educacional Aurora Ltda")]
    [property: NormalizeText] string Name,
    [property: SwaggerSchema("Trading name (nome fantasia), when it differs from the legal name.")]
    [property: SwaggerExample("Colégio Aurora")]
    [property: NormalizeText] string? TradeName,
    [property: SwaggerSchema("CNPJ or CPF, matching PersonType. Accepted formatted or bare; stored "
        + "normalized and validated with the check digits.")]
    [property: SwaggerExample("12.345.678/0001-95")]
    [property: NormalizeText] string Document,
    [property: SwaggerSchema("Contact e-mail.")]
    [property: SwaggerExample("contato@colegioaurora.com.br")]
    string? Email,
    [property: SwaggerSchema("Contact phone, with area code.")]
    [property: SwaggerExample("(51) 3477-9000")]
    string? Phone,
    [property: SwaggerSchema("Postal address.")]
    AddressRequest? Address,
    [property: SwaggerSchema("Credit ceiling for this customer, in BRL. Omit for no limit.")]
    [property: SwaggerExample("50000.00")]
    decimal? CreditLimit);

public sealed record EmployeeRequest(
    [property: SwaggerSchema("Company that employs the person.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Full name. Required.")]
    [property: SwaggerExample("Maria Aparecida de Souza")]
    [property: NormalizeText] string Name,
    [property: SwaggerSchema("CPF. Accepted formatted or bare; stored normalized and validated with the "
        + "check digits.")]
    [property: SwaggerExample("123.456.789-09")]
    [property: NormalizeText] string Document,
    [property: SwaggerSchema("Contact e-mail. This is registry data — it does not create a system login.")]
    [property: SwaggerExample("redacted-15@example.invalid")]
    string? Email,
    [property: SwaggerSchema("Contact phone, with area code.")]
    [property: SwaggerExample("(51) 99876-5432")]
    string? Phone,
    [property: SwaggerSchema("Job title (cargo). Free text, not a system role — it grants no permission.")]
    [property: SwaggerExample("Analista de Compras")]
    [property: NormalizeText] string Role,
    [property: SwaggerSchema("Unit the person works in.")]
    UnitId UnitId,
    [property: SwaggerSchema("Cost center the person's expenses are charged to. Optional.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("Hiring date (data de admissão).")]
    [property: SwaggerExample("2026-02-01")]
    DateOnly HireDate,
    [property: SwaggerSchema("Postal address.")]
    AddressRequest? Address);
