using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Contracts;

/// <summary>Address response shape. Matches the frontend <c>Address</c> type.</summary>
[SwaggerSchema("A Brazilian postal address, shared by every party in the registry. Always present on the "
    + "response even when the party was registered without one — the fields come back empty rather than "
    + "the object coming back null.")]
public sealed record AddressResponse(
    [property: SwaggerSchema("CEP, digits only.")]
    [property: SwaggerExample("92425900")]
    string Cep,
    [property: SwaggerSchema("Street name (logradouro), without the number.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string Street,
    [property: SwaggerSchema("Street number. Free text, since it is not always numeric.")]
    [property: SwaggerExample("8001")]
    string Number,
    [property: SwaggerSchema("Complement — block, room, floor.")]
    [property: SwaggerExample("Prédio 14, sala 210")]
    string? Complement,
    [property: SwaggerSchema("Neighborhood (bairro).")]
    [property: SwaggerExample("São José")]
    string Neighborhood,
    [property: SwaggerSchema("City name.")]
    [property: SwaggerExample("Canoas")]
    string City,
    [property: SwaggerSchema("State, as the two-letter UF.")]
    [property: SwaggerExample("RS")]
    string State)
{
    public static AddressResponse From(Address a) =>
        new(a.Cep, a.Street, a.Number, a.Complement, a.Neighborhood, a.City, a.State);
}

/// <summary>BankInfo response shape. Matches the frontend <c>BankInfo</c> type.</summary>
[SwaggerSchema("Where a vendor gets paid. Incomplete bank data is what makes a CNAB remittance refuse the "
    + "vendor, so treat empty fields as \"not payable by transfer yet\" rather than optional.")]
public sealed record BankInfoResponse(
    [property: SwaggerSchema("Bank code (COMPE), three digits.")]
    [property: SwaggerExample("341")]
    string BankCode,
    [property: SwaggerSchema("Branch (agência), without the check digit.")]
    [property: SwaggerExample("1234")]
    string Branch,
    [property: SwaggerSchema("Account number, without the check digit.")]
    [property: SwaggerExample("56789")]
    string Account,
    [property: SwaggerSchema("Account check digit, kept apart from the number because the CNAB layout "
        + "carries the two in separate fields.")]
    [property: SwaggerExample("0")]
    string AccountDigit,
    [property: SwaggerSchema("Pix key, in whatever form it was registered — CPF/CNPJ, e-mail, phone or "
        + "random key. Used when the payment goes out by Pix instead of the bank account above.")]
    [property: SwaggerExample("pagamentos@fornecedor.com.br")]
    string PixKey)
{
    public static BankInfoResponse From(BankInfo b) =>
        new(b.BankCode, b.Branch, b.Account, b.AccountDigit, b.PixKey);
}

/// <summary>Vendor response shape. Matches the frontend <c>Vendor</c> type.
/// <c>PersonType</c> serializes as an enum string via the global converter (current behavior).</summary>
[SwaggerSchema("A vendor (fornecedor) — who gets paid. Beyond identity and bank data it carries the tax "
    + "profile that decides which withholdings a payable calculates, so an incomplete vendor produces "
    + "wrong net amounts rather than an error.")]
public sealed record VendorResponse(
    VendorId Id,
    [property: SwaggerSchema("Whether the vendor is a company (CNPJ) or an individual (CPF). It decides "
        + "how Document is validated and which withholdings can apply.")]
    PersonType PersonType,
    [property: SwaggerSchema("Legal name (razão social for a company, full name for an individual).")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string Name,
    [property: SwaggerSchema("Trade name (nome fantasia), when it differs from the legal name.")]
    [property: SwaggerExample("Horizonte Engenharia")]
    string? TradeName,
    [property: SwaggerSchema("CNPJ or CPF, digits only — which one follows PersonType.")]
    [property: SwaggerExample("12345678000195")]
    string Document,
    [property: SwaggerSchema("Contact e-mail.")]
    [property: SwaggerExample("financeiro@horizonte.com.br")]
    string? Email,
    [property: SwaggerSchema("Contact phone, digits only, with area code.")]
    [property: SwaggerExample("5133451234")]
    string? Phone,
    [property: SwaggerSchema("Postal address. Comes back with empty fields rather than null when the "
        + "vendor was registered without one.")]
    AddressResponse? Address,
    [property: SwaggerSchema("Agreed payment terms, as free text. Descriptive only — nothing computes a "
        + "due date from it.")]
    [property: SwaggerExample("30/60/90 dias")]
    string? PaymentTerms,
    [property: SwaggerSchema("Bank and Pix data. Incomplete data is what makes a CNAB remittance refuse "
        + "this vendor.")]
    BankInfoResponse? BankInfo,
    [property: SwaggerSchema("Tax regime. It drives the withholding rules that apply — a Simples vendor "
        + "is treated differently from a Lucro Presumido one.")]
    TaxRegime TaxRegime,
    [property: SwaggerSchema("How this vendor's due dates are set: on the invoice's own terms, or pinned "
        + "to a fixed day of the month.")]
    PaymentScheduleMode PaymentScheduleMode,
    [property: SwaggerSchema("Day of month the payment is pinned to. Only meaningful when "
        + "PaymentScheduleMode pins to a fixed day; null otherwise.")]
    [property: SwaggerExample("10")]
    int? PaymentScheduleDay,
    [property: SwaggerSchema("Whether the vendor is in use. An inactive vendor is kept for history but "
        + "cannot be chosen on a new requisition or payable.")]
    bool Active,
    [property: SwaggerSchema("Whether the vendor is expected to fall under CPRB (desoneração da folha), "
        + "which suppresses the INSS withholding. Null means it was never determined — not that it is "
        + "false, so do not treat the two the same.")]
    bool? CprbForeseen,
    [property: SwaggerSchema("Simples Nacional annex the vendor falls in. Only meaningful when TaxRegime "
        + "is Simples; it selects the withholding table.")]
    SimplesAnexo? SimplesAnexo,
    [property: SwaggerSchema("Payment methods this vendor accepts. It constrains what an installment may "
        + "be paid by, and a remittance only covers the methods the bank layout supports.")]
    IReadOnlyList<PaymentMethod> AcceptedPaymentMethods,
    [property: SwaggerSchema("Per-tax rates registered for this vendor. They override the default rule "
        + "for the regime, which is how an exception negotiated with one vendor is expressed.")]
    IReadOnlyList<VendorWithholdingRate> WithholdingRates,
    [property: SwaggerSchema("Ledger accounts usually posted against for this vendor. A convenience for "
        + "the accounting review, which pre-selects them — not a restriction.")]
    IReadOnlyList<LedgerAccountId> AssociatedLedgerAccountIds,
    [property: SwaggerSchema("Primary CNAE code, as returned by the CNPJ lookup. It is what suggests the "
        + "service nature and the withholding profile.")]
    [property: SwaggerExample("4120400")]
    string? PrimaryCnaeCode,
    [property: SwaggerSchema("Human-readable description of the primary CNAE.")]
    [property: SwaggerExample("Construção de edifícios")]
    string? PrimaryCnaeDescription,
    // Registration timestamp — drives the "novo fornecedor" badge on the triage screen.
    [property: SwaggerSchema("When the vendor was registered. The triage screen uses it to flag a vendor "
        + "as new, so an approver knows the counterparty has no history yet.")]
    DateTimeOffset CreatedAt)
{
    public static VendorResponse From(VendorEntity v) => new(
        new VendorId(v.Id), v.PersonType, v.Name, v.TradeName, v.Document, v.Email, v.Phone,
        AddressResponse.From(v.Address), v.PaymentTerms, BankInfoResponse.From(v.BankInfo), v.TaxRegime,
        v.PaymentSchedule.Mode, v.PaymentSchedule.DayOfMonth, v.Active, v.CprbForeseen, v.SimplesAnexo,
        v.AcceptedPaymentMethods, v.WithholdingRates,
        v.AssociatedLedgerAccountIds.Select(id => new LedgerAccountId(id)).ToList(),
        v.PrimaryCnaeCode, v.PrimaryCnaeDescription,
        v.CreatedAt);
}

/// <summary>Customer response shape. Matches the frontend <c>Customer</c> type.</summary>
[SwaggerSchema("A customer (cliente) — who owes. The counterpart of a vendor, used by receivables.")]
public sealed record CustomerResponse(
    CustomerId Id,
    [property: SwaggerSchema("Company the customer is registered under. Customers are per-company.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Whether the customer is a company (CNPJ) or an individual (CPF).")]
    PersonType PersonType,
    [property: SwaggerSchema("Legal name (razão social for a company, full name for an individual).")]
    [property: SwaggerExample("Prefeitura Municipal de Canoas")]
    string Name,
    [property: SwaggerSchema("Trade name (nome fantasia), when it differs from the legal name.")]
    string? TradeName,
    [property: SwaggerSchema("CNPJ or CPF, digits only — which one follows PersonType.")]
    [property: SwaggerExample("88332580000165")]
    string Document,
    [property: SwaggerSchema("Contact e-mail.")]
    [property: SwaggerExample("contas@canoas.rs.gov.br")]
    string? Email,
    [property: SwaggerSchema("Contact phone, digits only, with area code.")]
    [property: SwaggerExample("5133501000")]
    string? Phone,
    [property: SwaggerSchema("Postal address. Comes back with empty fields rather than null when the "
        + "customer was registered without one.")]
    AddressResponse? Address,
    [property: SwaggerSchema("Credit ceiling agreed with the customer, in BRL. Null means none was set. "
        + "Informational — no receivable is blocked by it today.")]
    [property: SwaggerExample("150000.00")]
    decimal? CreditLimit,
    [property: SwaggerSchema("Whether the customer is in use. An inactive one is kept for history but "
        + "cannot be chosen on a new receivable.")]
    bool Active)
{
    public static CustomerResponse From(CustomerEntity c) => new(
        new CustomerId(c.Id), new CompanyId(c.CompanyId), c.PersonType, c.Name, c.TradeName, c.Document, c.Email, c.Phone,
        AddressResponse.From(c.Address), c.CreditLimit, c.Active);
}

/// <summary>Employee response shape. Matches the frontend <c>Employee</c> type.</summary>
[SwaggerSchema("An employee (colaborador). Registry data only — this is not the application user that "
    + "signs in, which lives in Identity and is keyed by Google account.")]
public sealed record EmployeeResponse(
    EmployeeId Id,
    [property: SwaggerSchema("Company the employee belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Full name.")]
    [property: SwaggerExample("João Pereira")]
    string Name,
    [property: SwaggerSchema("CPF, digits only.")]
    [property: SwaggerExample("12345678909")]
    string Document,
    [property: SwaggerSchema("Contact e-mail. Unrelated to the Google account used to sign in.")]
    [property: SwaggerExample("redacted-10@example.invalid")]
    string? Email,
    [property: SwaggerSchema("Contact phone, digits only, with area code.")]
    [property: SwaggerExample("5199998888")]
    string? Phone,
    [property: SwaggerSchema("Job title, as free text. Not the access role — that is on the user in "
        + "Identity, and nothing here grants permissions.")]
    [property: SwaggerExample("Analista de compras")]
    string Role,
    [property: SwaggerSchema("Unit the employee is assigned to.")]
    UnitId UnitId,
    [property: SwaggerSchema("Cost center the employee is charged to, when one applies.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("Hire date (data de admissão).")]
    [property: SwaggerExample("2024-03-01")]
    DateOnly HireDate,
    [property: SwaggerSchema("Postal address. Comes back with empty fields rather than null when the "
        + "employee was registered without one.")]
    AddressResponse? Address,
    [property: SwaggerSchema("Whether the employee is active. An inactive one is kept for history.")]
    bool Active)
{
    public static EmployeeResponse From(EmployeeEntity e) => new(
        new EmployeeId(e.Id), new CompanyId(e.CompanyId), e.Name, e.Document, e.Email, e.Phone, e.Role,
        new UnitId(e.UnitId), e.CostCenterId is { } cc ? new CostCenterId(cc) : null,
        e.HireDate, AddressResponse.From(e.Address), e.Active);
}
