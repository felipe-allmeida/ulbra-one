namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Vendor entities (wire form <c>vendor_{guid:N}</c>).</summary>
[PrefixedId("vendor_")]
public readonly partial record struct VendorId(Guid Value);
