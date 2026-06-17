namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for PayableAttachment entities (wire form <c>attachment_{guid:N}</c>).</summary>
[PrefixedId("attachment_")]
public readonly partial record struct PayableAttachmentId(Guid Value);
