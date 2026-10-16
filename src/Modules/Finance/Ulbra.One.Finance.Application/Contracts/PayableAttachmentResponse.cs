using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("Metadata of a file attached to a payable. The bytes come from the download endpoint.")]
public sealed record PayableAttachmentResponse(
    PayableAttachmentId Id,
    [property: SwaggerSchema("Original file name as uploaded.")]
    [property: SwaggerExample("nota-fiscal-12345.pdf")]
    string FileName,
    [property: SwaggerSchema("MIME type detected on upload, validated against the file's actual content "
        + "rather than trusted from the extension.")]
    [property: SwaggerExample("application/pdf")]
    string ContentType,
    [property: SwaggerSchema("File size in bytes.")]
    [property: SwaggerExample("184320")]
    long SizeBytes,
    [property: SwaggerSchema("What the file is (Nota Fiscal / Boleto / Fatura / Outro). Only FiscalDocument "
        + "clears the 'NF pendente' gate.")]
    PayableAttachmentKind Kind,
    [property: SwaggerSchema("When the file was uploaded.")]
    DateTimeOffset CreatedAt)
{
    public static PayableAttachmentResponse From(PayableAttachment a) =>
        new(new PayableAttachmentId(a.Id), a.FileName, a.ContentType, a.SizeBytes, a.Kind, a.CreatedAt);
}
