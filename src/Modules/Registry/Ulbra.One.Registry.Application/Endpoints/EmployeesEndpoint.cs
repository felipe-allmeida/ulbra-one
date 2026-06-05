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

public sealed class EmployeesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/employees").WithTags("Registry").RequireAuthorization();

        group.MapGet("/", async (Guid? companyId, string? search, [AsParameters] PaginationQueryParameters pagination, RegistryContext db, CancellationToken ct) =>
        {
            var query = db.Employees.AsQueryable();
            if (companyId is { } cid) query = query.Where(e => e.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(e => e.Name.Contains(search) || e.Document.Contains(search));
            var page = await query.OrderBy(e => e.Name).ToPaginatedAsync(pagination, ct);
            return Results.Ok(page.Map(EmployeeResponse.From));
        })
            .WithName("ListEmployees")
            .Produces<PaginatedResponse<EmployeeResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            return (employee is null
                ? Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found."))
                : Result.Success(EmployeeResponse.From(employee))).ToOk();
        })
            .WithName("GetEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (EmployeeRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var employee = new EmployeeEntity(req.CompanyId, req.Name, req.Document, req.Email, req.Phone,
                req.Role, req.UnitId, req.CostCenterId, req.HireDate, req.Address.ToAddress());
            db.Employees.Add(employee);
            await db.SaveChangesAsync(ct);
            return Result.Success(EmployeeResponse.From(employee)).ToCreated(e => $"/api/employees/{e.Id}");
        }).WithValidation<EmployeeRequest>().WithName("CreateEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (Guid id, EmployeeRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
            employee.Update(req.Name, req.Document, req.Email, req.Phone, req.Role,
                req.UnitId, req.CostCenterId, req.HireDate, req.Address.ToAddress());
            await db.SaveChangesAsync(ct);
            return Result.Success(EmployeeResponse.From(employee)).ToOk();
        }).WithValidation<EmployeeRequest>().WithName("UpdateEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
            employee.Deactivate();
            await db.SaveChangesAsync(ct);
            return Result.Success(EmployeeResponse.From(employee)).ToOk();
        }).WithName("DeactivateEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
            employee.Activate();
            await db.SaveChangesAsync(ct);
            return Result.Success(EmployeeResponse.From(employee)).ToOk();
        }).WithName("ActivateEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, RegistryContext db, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
            employee.MarkDeleted(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Result.Success().ToOk();
        }).WithName("DeleteEmployee")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
