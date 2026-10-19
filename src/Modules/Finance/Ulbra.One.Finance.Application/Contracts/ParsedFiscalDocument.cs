using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One installment (duplicata) read from the fiscal document — due date + amount.</summary>
[SwaggerSchema("One duplicata read from the fiscal document.")]
public sealed record ParsedInstallment(
    [property: SwaggerSchema("Due date as stated on the document.")]
    [property: SwaggerExample("2026-09-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Installment amount as stated, in BRL.")]
    [property: SwaggerExample("1500.00")]
    decimal Amount);

/// <summary>Issuer address read from the fiscal document (NF-e enderEmit) — used to pre-fill a vendor. No complement.</summary>
[SwaggerSchema("Issuer address read from the document, to pre-fill a new vendor. Every field is "
    + "nullable because a document may simply not carry it — and there is no complement in the NF-e layout.")]
public sealed record ParsedAddress(
    [property: SwaggerSchema("CEP, digits only.")]
    [property: SwaggerExample("92425900")]
    string? Cep,
    [property: SwaggerSchema("Street name.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string? Street,
    [property: SwaggerSchema("Street number.")]
    [property: SwaggerExample("8001")]
    string? Number,
    [property: SwaggerSchema("Neighborhood.")]
    [property: SwaggerExample("São José")]
    string? Neighborhood,
    [property: SwaggerSchema("City name.")]
    [property: SwaggerExample("Canoas")]
    string? City,
    [property: SwaggerSchema("State, as the two-letter UF.")]
    [property: SwaggerExample("RS")]
    string? State);

/// <summary>
/// Result of parsing a fiscal document (NF-e/NFS-e XML, or PDF/image via the AI vision extractor). Returned by the
/// stateless parse/preview endpoint to pre-fill the payable form — nothing is persisted at this point.
/// The parsers leave the vendor-match fields null; the endpoint fills them from the registry.
/// </summary>
[SwaggerSchema("What was read out of a fiscal document, to pre-fill the payable form. NOTHING is "
    + "persisted by the parse — it is a preview, and every field is a suggestion the operator can "
    + "override. Which fields come back depends on the format: the NFS-e ones are null for an NF-e and "
    + "vice versa, so absence means \"not applicable here\", not \"missing from the document\".")]
public sealed record ParsedFiscalDocument(
    [property: SwaggerSchema("What was parsed: an NF-e XML, an NFS-e XML, or a PDF/image read by the "
        + "vision extractor. It is what decides which of the fields below can be populated at all.")]
    FiscalDocumentFormat Format,
    [property: SwaggerSchema("Chave de acesso, 44 digits. XML only.")]
    [property: SwaggerExample("43260888332580000165550010000123451000123456")]
    string? AccessKey,
    [property: SwaggerSchema("Invoice number.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Emission date.")]
    [property: SwaggerExample("2026-08-05")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("Document total, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal? TotalAmount,
    [property: SwaggerSchema("Issuer CNPJ or CPF, digits only — the vendor. It is what the registry match "
        + "below is keyed on.")]
    [property: SwaggerExample("12345678000195")]
    string? IssuerDocument,
    [property: SwaggerSchema("Issuer legal name.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string? IssuerName,
    [property: SwaggerSchema("Recipient CNPJ, digits only — which of our companies the document was "
        + "issued against.")]
    [property: SwaggerExample("88332580000165")]
    string? RecipientDocument,
    [property: SwaggerSchema("The extraction as raw JSON, exactly as the parser produced it. Kept so a "
        + "field the form does not use is still available for auditing what was read.")]
    string RawExtractionJson,
    [property: SwaggerSchema("Duplicatas found on the document. Empty when it carries none, which means "
        + "the operator has to author the plan.")]
    IReadOnlyList<ParsedInstallment> Installments,
    [property: SwaggerSchema("Vendor matched in the registry by the issuer document. Null means no "
        + "vendor is registered yet — the form then offers to create one.")]
    VendorId? MatchedVendorId = null,
    [property: SwaggerSchema("Name of the matched vendor as registered, which may differ from IssuerName "
        + "on the document.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string? MatchedVendorName = null,
    // NFS-e (service) — informational, shown in the preview; null for NF-e/PDF.
    [property: SwaggerSchema("RPS number. NFS-e only, informational.")]
    [property: SwaggerExample("4821")]
    string? RpsNumber = null,
    [property: SwaggerSchema("RPS series. NFS-e only, informational.")]
    [property: SwaggerExample("1")]
    string? RpsSeries = null,
    [property: SwaggerSchema("Service description as written on the document. NFS-e only.")]
    [property: SwaggerExample("Manutenção preventiva de sistema de climatização")]
    string? ServiceDescription = null,
    [property: SwaggerSchema("CNAE code stated on the document. NFS-e only.")]
    [property: SwaggerExample("4322302")]
    string? CnaeCode = null,
    // NFS-e (service) — tax signals promoted to columns; inputs for the withholding calculation.
    // Lc116Subitem (ItemListaServico) is reliable; the others are optional in ABRASF (best-effort for ISS).
    [property: SwaggerSchema("LC 116 service subitem. The most reliable of the NFS-e tax signals, and the "
        + "main input to the withholding rules.")]
    [property: SwaggerExample("7.02")]
    string? Lc116Subitem = null,
    [property: SwaggerSchema("Municipal service code. Optional in ABRASF, so it is best-effort.")]
    string? MunicipalServiceCode = null,
    [property: SwaggerSchema("IBGE code of the municipality where the service was rendered — it decides "
        + "the ISS rate. Optional in ABRASF, so best-effort.")]
    [property: SwaggerExample("4304606")]
    string? ServiceMunicipalityIbge = null,
    [property: SwaggerSchema("ISS rate stated on the invoice, as a percentage. Best-effort.")]
    [property: SwaggerExample("3.00")]
    decimal? InvoiceIssRate = null,
    [property: SwaggerSchema("Whether the invoice itself declares ISS as withheld. Null means the "
        + "document did not say — which is not the same as false, and the rules treat the two "
        + "differently.")]
    bool? IssWithheldOnInvoice = null,
    // Issuer contact (NF-e emit) — feeds the "cadastrar fornecedor" pre-fill; null for NFS-e/PDF.
    [property: SwaggerSchema("Issuer phone, to pre-fill a new vendor. NF-e only.")]
    [property: SwaggerExample("5133451234")]
    string? IssuerPhone = null,
    [property: SwaggerSchema("Issuer address, to pre-fill a new vendor. NF-e only.")]
    ParsedAddress? IssuerAddress = null,
    // Fiscal signals (NF-e): distinct CFOPs/NCMs across items + ICMSTot tax totals; null for NFS-e/PDF.
    [property: SwaggerSchema("Document-level fiscal signals. NF-e only.")]
    FiscalSummary? Fiscal = null,
    // Payable type (Service/Product) inferred by the AI vision extractor for a PDF/image; null for XML
    // (derived from Format at upload) and when the model is unsure. Already validated against PayableType.
    [property: SwaggerSchema("Service or product, inferred by the vision extractor for a PDF or image. "
        + "Null for XML, where the format already settles it, and null when the model was unsure — so a "
        + "null here means undetermined, and the operator has to choose.")]
    PayableType? SuggestedType = null);

/// <summary>Document-level fiscal signals (NF-e). Distinct CFOPs/NCMs across items + ICMSTot tax totals.</summary>
[SwaggerSchema("Document-level fiscal signals from an NF-e: the distinct codes across its items and the "
    + "tax totals. These are what the issuer declared — they are not recomputed here.")]
public sealed record FiscalSummary(
    [property: SwaggerSchema("Distinct CFOPs across the document's items. More than one means the "
        + "document mixes operation types.")]
    IReadOnlyList<string> Cfops,
    [property: SwaggerSchema("Distinct NCMs across the document's items.")]
    IReadOnlyList<string> Ncms,
    [property: SwaggerSchema("ICMS total declared on the document, in BRL.")]
    [property: SwaggerExample("810.00")]
    decimal? Icms,
    [property: SwaggerSchema("IPI total declared, in BRL.")]
    decimal? Ipi,
    [property: SwaggerSchema("PIS total declared, in BRL.")]
    decimal? Pis,
    [property: SwaggerSchema("COFINS total declared, in BRL.")]
    decimal? Cofins,
    [property: SwaggerSchema("Sum of the taxes declared on the document, in BRL. Unrelated to what WE "
        + "withhold — those are computed by the withholding rules, not read from here.")]
    [property: SwaggerExample("1024.50")]
    decimal? TotalTaxes);
