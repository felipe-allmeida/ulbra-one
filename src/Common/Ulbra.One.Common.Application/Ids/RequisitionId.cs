namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Requisition entities (wire form <c>req_{guid:N}</c>).</summary>
[PrefixedId("req_")]
public readonly partial record struct RequisitionId(Guid Value);
