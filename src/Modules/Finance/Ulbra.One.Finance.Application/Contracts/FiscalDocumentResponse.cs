using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>The fiscal document of record attached to a payable (parsed fields + stored-file metadata).</summary>
public sealed record FiscalDocumentResponse(
    FiscalDocumentId Id, PayableId PayableId, FiscalDocumentFormat Format, string? AccessKey,
    string? DocumentNumber, DateOnly? IssueDate, decimal? TotalAmount, string? IssuerDocument,
    string? IssuerName, string? RecipientDocument, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset CreatedAt)
{
    public static FiscalDocumentResponse From(FiscalDocument d) => new(
        new FiscalDocumentId(d.Id), new PayableId(d.PayableId), d.Format, d.AccessKey, d.DocumentNumber,
        d.IssueDate, d.TotalAmount, d.IssuerDocument, d.IssuerName, d.RecipientDocument, d.FileName,
        d.ContentType, d.SizeBytes, d.CreatedAt);
}
