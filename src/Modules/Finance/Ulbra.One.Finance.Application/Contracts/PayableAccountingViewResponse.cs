using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Vendor tax/accounting data shown on the "Registro de Despesa" (accounting) screen.</summary>
[SwaggerSchema("The vendor's tax profile as the accounting screen needs it — the fields that explain "
    + "why a given withholding applied.")]
public sealed record PayableVendorTaxBlock(
    [property: SwaggerSchema("Whether the vendor is a company or an individual.")]
    PersonType PersonType,
    [property: SwaggerSchema("Legal name.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string Name,
    [property: SwaggerSchema("Trade name, when it differs.")]
    string? TradeName,
    [property: SwaggerSchema("CNPJ or CPF, digits only.")]
    [property: SwaggerExample("12345678000195")]
    string Document,
    [property: SwaggerSchema("Contact e-mail.")]
    string? Email,
    [property: SwaggerSchema("Contact phone, digits only.")]
    string? Phone,
    [property: SwaggerSchema("Tax regime — the main driver of which withholding rules matched.")]
    TaxRegime TaxRegime,
    [property: SwaggerSchema("Simples Nacional annex. Only meaningful when the regime is Simples.")]
    SimplesAnexo? SimplesAnexo,
    [property: SwaggerSchema("Whether the vendor is expected to fall under CPRB, which suppresses INSS. "
        + "Null means never determined — not false.")]
    bool? CprbForeseen,
    [property: SwaggerSchema("Vendor city — it matters for ISS, whose rate is set municipally.")]
    [property: SwaggerExample("Canoas")]
    string City,
    [property: SwaggerSchema("Vendor state (UF).")]
    [property: SwaggerExample("RS")]
    string State);

/// <summary>Composite read model for the accounting stage ("Registro de Despesa"): the payable's money
/// picture (bruto/retido/líquido), its ledger classification, retentions, the vendor's tax data, and the
/// registered fiscal document (Nota Fiscal) — assembled in a single call.</summary>
[SwaggerSchema("Everything the Registro de Despesa screen needs about one payable, assembled in a "
    + "single call: the money picture, the classification, the retentions, the vendor's tax profile and "
    + "the Nota Fiscal.")]
public sealed record PayableAccountingViewResponse(
    PayableId Id,
    [property: SwaggerSchema("Where the payable stands.")]
    PayableStatus Status,
    [property: SwaggerSchema("How the payable came to exist.")]
    PayableOrigin Origin,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Document total, in BRL — bruto.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Sum of the withholdings, in BRL — retido.")]
    [property: SwaggerExample("209.25")]
    decimal TotalWithheld,
    [property: SwaggerSchema("What the vendor receives, in BRL — líquido.")]
    [property: SwaggerExample("4290.75")]
    decimal NetTotal,
    [property: SwaggerSchema("Ledger account applied. Null while pending classification.")]
    LedgerAccountId? AccountingAccountId,
    [property: SwaggerSchema("Code of that account, resolved so the screen does not need a second call.")]
    [property: SwaggerExample("3.1.2.04")]
    string? AccountingAccountCode,
    [property: SwaggerSchema("Name of that account.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string? AccountingAccountName,
    [property: SwaggerSchema("The withholdings applied to this payable.")]
    IReadOnlyList<PayableWithholdingResponse> Withholdings,
    [property: SwaggerSchema("Vendor tax profile. Null when the vendor could not be resolved from the "
        + "registry.")]
    PayableVendorTaxBlock? Vendor,
    [property: SwaggerSchema("The registered Nota Fiscal. Null when none is registered.")]
    FiscalDocumentResponse? FiscalDocument,
    [property: SwaggerSchema("Whether a Nota Fiscal is registered — the same fact as FiscalDocument being "
        + "non-null, kept so a list can check it without pulling the whole document.")]
    bool HasFiscalDocument);
