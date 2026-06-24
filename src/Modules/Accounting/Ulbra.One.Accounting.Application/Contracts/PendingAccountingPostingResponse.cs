using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>A payable accounting posting the system could not auto-resolve (open, i.e. <c>ResolvedAt == null</c>).</summary>
public sealed record PendingAccountingPostingResponse(
    PendingAccountingPostingId Id,
    CompanyId CompanyId,
    string SourceKey,
    PostingKind Kind,
    decimal Amount,
    string Reason,
    string IntendedCodes,
    DateTimeOffset CreatedAt)
{
    public static PendingAccountingPostingResponse From(PendingAccountingPosting p) => new(
        new PendingAccountingPostingId(p.Id),
        new CompanyId(p.CompanyId),
        p.SourceKey,
        p.Kind,
        p.Amount,
        p.Reason,
        p.IntendedCodes,
        p.CreatedAt);
}
