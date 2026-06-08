using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record PayableAttachmentResponse(
    PayableAttachmentId Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt)
{
    public static PayableAttachmentResponse From(PayableAttachment a) =>
        new(new PayableAttachmentId(a.Id), a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt);
}
