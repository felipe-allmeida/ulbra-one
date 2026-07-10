using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Vendor tax/accounting data shown on the "Registro de Despesa" (accounting) screen.</summary>
public sealed record PayableVendorTaxBlock(
    PersonType PersonType, string Name, string? TradeName, string Document,
    string? Email, string? Phone, TaxRegime TaxRegime,
    SimplesAnexo? SimplesAnexo, bool? CprbForeseen, string City, string State);

/// <summary>Composite read model for the accounting stage ("Registro de Despesa"): the payable's money
/// picture (bruto/retido/líquido), its ledger classification, retentions, the vendor's tax data, and the
/// registered fiscal document (Nota Fiscal) — assembled in a single call.</summary>
public sealed record PayableAccountingViewResponse(
    PayableId Id,
    PayableStatus Status,
    string CounterpartyName,
    string? DocumentNumber,
    decimal TotalAmount,
    decimal TotalWithheld,
    decimal NetTotal,
    LedgerAccountId? AccountingAccountId,
    string? AccountingAccountCode,
    string? AccountingAccountName,
    IReadOnlyList<PayableWithholdingResponse> Withholdings,
    PayableVendorTaxBlock? Vendor,
    FiscalDocumentResponse? FiscalDocument,
    bool HasFiscalDocument);
