using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Results;
using Ulbra.One.Common.Infrastructure.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Infrastructure.Database;

namespace Ulbra.One.Registry.Application.Endpoints;

public sealed class CustomersEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/customers").WithTags("Registry").RequireAuthorization();

        group.MapGet("/", async (Guid? companyId, string? search, [AsParameters] PaginationQueryParameters pagination, RegistryContext db, CancellationToken ct) =>
        {
            var query = db.Customers.AsQueryable();
            if (companyId is { } cid) query = query.Where(c => c.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search) || c.Document.Contains(search));
            var page = await query.OrderBy(c => c.Name).ToPaginatedAsync(pagination, ct);
            return Results.Ok(page.Map(CustomerResponse.From));
        })
            .WithName("ListCustomers")
            .Produces<PaginatedResponse<CustomerResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            return (customer is null
                ? Result.Failure<CustomerResponse>(Error.NotFound("Customer.NotFound", "Customer not found."))
                : Result.Success(CustomerResponse.From(customer))).ToOk();
        })
            .WithName("GetCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CustomerRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var customer = new CustomerEntity(req.CompanyId, req.PersonType, req.Name, req.TradeName,
                req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.CreditLimit);
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
            return Result.Success(CustomerResponse.From(customer)).ToCreated(c => $"/api/customers/{c.Id}");
        }).WithValidation<CustomerRequest>().WithName("CreateCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (Guid id, CustomerRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerResponse>(Error.NotFound("Customer.NotFound", "Customer not found.")).ToOk();
            customer.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
                req.Address.ToAddress(), req.CreditLimit);
            await db.SaveChangesAsync(ct);
            return Result.Success(CustomerResponse.From(customer)).ToOk();
        }).WithValidation<CustomerRequest>().WithName("UpdateCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerResponse>(Error.NotFound("Customer.NotFound", "Customer not found.")).ToOk();
            customer.Deactivate();
            await db.SaveChangesAsync(ct);
            return Result.Success(CustomerResponse.From(customer)).ToOk();
        }).WithName("DeactivateCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure<CustomerResponse>(Error.NotFound("Customer.NotFound", "Customer not found.")).ToOk();
            customer.Activate();
            await db.SaveChangesAsync(ct);
            return Result.Success(CustomerResponse.From(customer)).ToOk();
        }).WithName("ActivateCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, RegistryContext db, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null)
                return Result.Failure(Error.NotFound("Customer.NotFound", "Customer not found.")).ToOk();
            customer.MarkDeleted(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Result.Success().ToOk();
        }).WithName("DeleteCustomer")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
