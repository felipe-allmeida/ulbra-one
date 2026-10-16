namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for RequisitionAttachment entities (wire form <c>reqatt_{guid:N}</c>).</summary>
[PrefixedId("reqatt_")]
public readonly partial record struct RequisitionAttachmentId(Guid Value);
