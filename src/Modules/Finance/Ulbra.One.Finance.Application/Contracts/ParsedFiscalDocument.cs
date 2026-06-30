using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One installment (duplicata) read from the fiscal document — due date + amount.</summary>
public sealed record ParsedInstallment(DateOnly DueDate, decimal Amount);

/// <summary>Issuer address read from the fiscal document (NF-e enderEmit) — used to pre-fill a vendor. No complement.</summary>
public sealed record ParsedAddress(
    string? Cep, string? Street, string? Number, string? Neighborhood, string? City, string? State);

/// <summary>
/// Result of parsing a fiscal document (NF-e/NFS-e XML or the service PDF mock). Returned by the
/// stateless parse/preview endpoint to pre-fill the payable form — nothing is persisted at this point.
/// The parsers leave the vendor-match fields null; the endpoint fills them from the registry.
/// </summary>
public sealed record ParsedFiscalDocument(
    FiscalDocumentFormat Format,
    string? AccessKey,
    string? DocumentNumber,
    DateOnly? IssueDate,
    decimal? TotalAmount,
    string? IssuerDocument,
    string? IssuerName,
    string? RecipientDocument,
    string RawExtractionJson,
    IReadOnlyList<ParsedInstallment> Installments,
    VendorId? MatchedVendorId = null,
    string? MatchedVendorName = null,
    // NFS-e (serviço) — informativos, exibidos no preview; null para NF-e/PDF.
    string? RpsNumber = null,
    string? RpsSeries = null,
    string? ServiceDescription = null,
    string? CnaeCode = null,
    // Issuer contact (NF-e emit) — feeds the "cadastrar fornecedor" pre-fill; null for NFS-e/PDF.
    string? IssuerPhone = null,
    ParsedAddress? IssuerAddress = null,
    // Fiscal signals (NF-e): distinct CFOPs/NCMs across items + ICMSTot tax totals; null for NFS-e/PDF.
    FiscalSummary? Fiscal = null,
    // Tax withholdings declared in the document (retenções na fonte). TotalAmount stays gross;
    // the net is gross − Σ withholdings. Empty when nothing is withheld. Consumed downstream to
    // pre-fill the payable's withholdings (Payable.SetWithholdings) — not wired in this layer.
    IReadOnlyList<ParsedWithholding>? Withholdings = null);

/// <summary>Document-level fiscal signals (NF-e). Distinct CFOPs/NCMs across items + ICMSTot tax totals.</summary>
public sealed record FiscalSummary(
    IReadOnlyList<string> Cfops,
    IReadOnlyList<string> Ncms,
    decimal? Icms,
    decimal? Ipi,
    decimal? Pis,
    decimal? Cofins,
    decimal? TotalTaxes);

/// <summary>
/// A single tax withholding read from the fiscal document (retenção na fonte, retida pelo tomador).
/// Mirrors the tuple accepted by <c>Payable.SetWithholdings</c> so the consumer can map it 1:1.
/// </summary>
public sealed record ParsedWithholding(
    WithholdingTaxType TaxType, decimal BaseAmount, decimal? Rate, decimal Amount);
