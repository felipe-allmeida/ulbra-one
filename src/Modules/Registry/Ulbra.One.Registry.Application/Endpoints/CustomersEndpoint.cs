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

public sealed class CustomersEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/customers").WithTags("Registry").RequireAuthorization();

        group.MapGet("/", async (Guid? companyId, string? search, RegistryContext db, CancellationToken ct) =>
        {
            var query = db.Customers.AsQueryable();
            if (companyId is { } cid) query = query.Where(c => c.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search) || c.Document.Contains(search));
            var items = await query.OrderBy(c => c.Name).ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("ListCustomers");

        group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            return (customer is null
                ? Result.Failure<CustomerEntity>(Error.NotFound("Customer.NotFound", "Cliente não encontrado."))
                : Result.Success(customer)).ToOk();
        }).WithName("GetCustomer");

        group.MapPost("/", async (CustomerRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var customer = new CustomerEntity(req.CompanyId, req.PersonType, req.Name, req.TradeName,
                req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.CreditLimit);
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
            return Result.Success(customer).ToCreated(c => $"/api/customers/{c.Id}");
        }).WithValidation<CustomerRequest>().WithName("CreateCustomer");

        group.MapPut("/{id:guid}", async (Guid id, CustomerRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerEntity>(Error.NotFound("Customer.NotFound", "Cliente não encontrado.")).ToOk();
            customer.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
                req.Address.ToAddress(), req.CreditLimit);
            await db.SaveChangesAsync(ct);
            return Result.Success(customer).ToOk();
        }).WithValidation<CustomerRequest>().WithName("UpdateCustomer");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerEntity>(Error.NotFound("Customer.NotFound", "Cliente não encontrado.")).ToOk();
            customer.Deactivate();
            await db.SaveChangesAsync(ct);
            return Result.Success(customer).ToOk();
        }).WithName("DeactivateCustomer");

        group.MapPost("/{id:guid}/activate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerEntity>(Error.NotFound("Customer.NotFound", "Cliente não encontrado.")).ToOk();
            customer.Activate();
            await db.SaveChangesAsync(ct);
            return Result.Success(customer).ToOk();
        }).WithName("ActivateCustomer");

        group.MapDelete("/{id:guid}", async (Guid id, RegistryContext db, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure(Error.NotFound("Customer.NotFound", "Cliente não encontrado.")).ToOk();
            customer.MarkDeleted(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Result.Success().ToOk();
        }).WithName("DeleteCustomer");
    }
}
