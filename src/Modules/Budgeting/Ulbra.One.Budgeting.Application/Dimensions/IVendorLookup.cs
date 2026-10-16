namespace Ulbra.One.Budgeting.Application.Dimensions;

/// <summary>Cross-module port (adapter in Infrastructure → Registry) that validates a requisition's vendor
/// without the Application layer referencing Registry types. Mirrors <see cref="IBudgetDimensionLookup"/>:
/// read-only, no FK. Vendors are global to the organization, so a requisition may name any existing (live)
/// vendor regardless of company.</summary>
public interface IVendorLookup
{
    /// <summary>Whether <paramref name="vendorId"/> is an existing (live) vendor.</summary>
    Task<bool> ExistsAsync(Guid vendorId, CancellationToken ct);
}
