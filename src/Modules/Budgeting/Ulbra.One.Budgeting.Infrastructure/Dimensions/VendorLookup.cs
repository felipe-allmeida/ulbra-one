using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Budgeting.Infrastructure.Dimensions;

/// <summary>Registry-backed implementation of <see cref="IVendorLookup"/>. A deliberate cross-module type
/// reference (Budgeting.Infrastructure → Registry.Domain), read-only and no FK — the same lookup-adapter
/// exception as <see cref="BudgetDimensionLookup"/> → Core.Domain. The soft-delete query filter on
/// <c>VendorEntity</c> means a removed vendor won't validate.</summary>
public sealed class VendorLookup(IUnitOfWork uow) : IVendorLookup
{
    public Task<bool> ExistsAsync(Guid vendorId, CancellationToken ct) =>
        uow.ReadSet<VendorEntity>().AnyAsync(v => v.Id == vendorId, ct);
}
