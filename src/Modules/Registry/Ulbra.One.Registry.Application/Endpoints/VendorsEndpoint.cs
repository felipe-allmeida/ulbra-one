using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Results;
using Ulbra.One.Common.Infrastructure.Validation;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Infrastructure.Database;

namespace Ulbra.One.Registry.Application.Endpoints;

public sealed class VendorsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/vendors").WithTags("Registry").RequireAuthorization();

        group.MapGet("/", async (Guid? companyId, string? search, RegistryContext db, CancellationToken ct) =>
        {
            var query = db.Vendors.AsQueryable();
            if (companyId is { } cid) query = query.Where(v => v.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(v => v.Name.Contains(search) || v.Document.Contains(search));
            var items = await query.OrderBy(v => v.Name).ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("ListVendors");

        group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            return (vendor is null
                ? Result.Failure<VendorEntity>(Error.NotFound("Vendor.NotFound", "Fornecedor não encontrado."))
                : Result.Success(vendor)).ToOk();
        }).WithName("GetVendor");

        group.MapPost("/", async (VendorRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var vendor = new VendorEntity(req.CompanyId, req.PersonType, req.Name, req.TradeName,
                req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.PaymentTerms, req.BankInfo);
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync(ct);
            return Result.Success(vendor).ToCreated(v => $"/api/vendors/{v.Id}");
        }).WithValidation<VendorRequest>().WithName("CreateVendor");

        group.MapPut("/{id:guid}", async (Guid id, VendorRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null)
                return Result.Failure<VendorEntity>(Error.NotFound("Vendor.NotFound", "Fornecedor não encontrado.")).ToOk();
            vendor.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
                req.Address.ToAddress(), req.PaymentTerms, req.BankInfo);
            await db.SaveChangesAsync(ct);
            return Result.Success(vendor).ToOk();
        }).WithValidation<VendorRequest>().WithName("UpdateVendor");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null)
                return Result.Failure<VendorEntity>(Error.NotFound("Vendor.NotFound", "Fornecedor não encontrado.")).ToOk();
            vendor.Deactivate();
            await db.SaveChangesAsync(ct);
            return Result.Success(vendor).ToOk();
        }).WithName("DeactivateVendor");

        group.MapPost("/{id:guid}/activate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null)
                return Result.Failure<VendorEntity>(Error.NotFound("Vendor.NotFound", "Fornecedor não encontrado.")).ToOk();
            vendor.Activate();
            await db.SaveChangesAsync(ct);
            return Result.Success(vendor).ToOk();
        }).WithName("ActivateVendor");

        group.MapDelete("/{id:guid}", async (Guid id, RegistryContext db, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null)
                return Result.Failure(Error.NotFound("Vendor.NotFound", "Fornecedor não encontrado.")).ToOk();
            vendor.MarkDeleted(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Result.Success().ToOk();
        }).WithName("DeleteVendor");
    }
}
