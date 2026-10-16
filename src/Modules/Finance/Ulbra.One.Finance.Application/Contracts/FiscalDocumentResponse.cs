using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>The fiscal document of record attached to a payable (parsed fields + stored-file metadata).</summary>
[SwaggerSchema("The Nota Fiscal of record attached to a payable: the parsed fields plus the stored "
    + "file's metadata. Unlike the parse preview, this one is persisted — it is what the payable is "
    + "documented by.")]
public sealed record FiscalDocumentResponse(
    FiscalDocumentId Id,
    [property: SwaggerSchema("Payable this document belongs to.")]
    PayableId PayableId,
    [property: SwaggerSchema("What kind of document it is. It decides which of the fields below are "
        + "meaningful.")]
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
    [property: SwaggerSchema("Document total, in BRL, as stated on the invoice. It may differ from the "
        + "payable's total — nothing forces them equal.")]
    [property: SwaggerExample("4500.00")]
    decimal? TotalAmount,
    [property: SwaggerSchema("Issuer CNPJ or CPF, digits only.")]
    [property: SwaggerExample("12345678000195")]
    string? IssuerDocument,
    [property: SwaggerSchema("Issuer legal name.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string? IssuerName,
    [property: SwaggerSchema("Recipient CNPJ, digits only.")]
    [property: SwaggerExample("88332580000165")]
    string? RecipientDocument,
    [property: SwaggerSchema("Stored file name.")]
    [property: SwaggerExample("nfe-12345.xml")]
    string FileName,
    [property: SwaggerSchema("MIME type of the stored file.")]
    [property: SwaggerExample("application/xml")]
    string ContentType,
    [property: SwaggerSchema("File size in bytes.")]
    [property: SwaggerExample("18240")]
    long SizeBytes,
    [property: SwaggerSchema("When the document was registered against the payable.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Distinct CFOPs across the document's items. NF-e only.")]
    IReadOnlyList<string> Cfops,
    [property: SwaggerSchema("Distinct NCMs across the document's items. NF-e only.")]
    IReadOnlyList<string> Ncms,
    [property: SwaggerSchema("ICMS declared on the document, in BRL.")]
    [property: SwaggerExample("810.00")]
    decimal? IcmsAmount,
    [property: SwaggerSchema("IPI declared, in BRL.")]
    decimal? IpiAmount,
    [property: SwaggerSchema("PIS declared, in BRL.")]
    decimal? PisAmount,
    [property: SwaggerSchema("COFINS declared, in BRL.")]
    decimal? CofinsAmount,
    [property: SwaggerSchema("Total taxes declared on the document, in BRL. What the ISSUER declared — "
        + "unrelated to what we withhold, which the withholding rules compute separately.")]
    [property: SwaggerExample("1024.50")]
    decimal? TotalTaxesAmount)
{
    public static FiscalDocumentResponse From(FiscalDocument d) => new(
        new FiscalDocumentId(d.Id), new PayableId(d.PayableId), d.Format, d.AccessKey, d.DocumentNumber,
        d.IssueDate, d.TotalAmount, d.IssuerDocument, d.IssuerName, d.RecipientDocument, d.FileName,
        d.ContentType, d.SizeBytes, d.CreatedAt,
        d.Cfops, d.Ncms, d.IcmsAmount, d.IpiAmount, d.PisAmount, d.CofinsAmount, d.TotalTaxesAmount);
}
