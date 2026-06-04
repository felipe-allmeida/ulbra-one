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

public sealed class EmployeesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/employees").WithTags("Registry").RequireAuthorization();

        group.MapGet("/", async (Guid? companyId, string? search, RegistryContext db, CancellationToken ct) =>
        {
            var query = db.Employees.AsQueryable();
            if (companyId is { } cid) query = query.Where(e => e.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(e => e.Name.Contains(search) || e.Document.Contains(search));
            var items = await query.OrderBy(e => e.Name).ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("ListEmployees");

        group.MapGet("/{id:guid}", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            return (employee is null
                ? Result.Failure<EmployeeEntity>(Error.NotFound("Employee.NotFound", "Colaborador não encontrado."))
                : Result.Success(employee)).ToOk();
        }).WithName("GetEmployee");

        group.MapPost("/", async (EmployeeRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var employee = new EmployeeEntity(req.CompanyId, req.Name, req.Document, req.Email, req.Phone,
                req.Role, req.UnitId, req.CostCenterId, req.HireDate, req.Address.ToAddress());
            db.Employees.Add(employee);
            await db.SaveChangesAsync(ct);
            return Result.Success(employee).ToCreated(e => $"/api/employees/{e.Id}");
        }).WithValidation<EmployeeRequest>().WithName("CreateEmployee");

        group.MapPut("/{id:guid}", async (Guid id, EmployeeRequest req, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeEntity>(Error.NotFound("Employee.NotFound", "Colaborador não encontrado.")).ToOk();
            employee.Update(req.Name, req.Document, req.Email, req.Phone, req.Role,
                req.UnitId, req.CostCenterId, req.HireDate, req.Address.ToAddress());
            await db.SaveChangesAsync(ct);
            return Result.Success(employee).ToOk();
        }).WithValidation<EmployeeRequest>().WithName("UpdateEmployee");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeEntity>(Error.NotFound("Employee.NotFound", "Colaborador não encontrado.")).ToOk();
            employee.Deactivate();
            await db.SaveChangesAsync(ct);
            return Result.Success(employee).ToOk();
        }).WithName("DeactivateEmployee");

        group.MapPost("/{id:guid}/activate", async (Guid id, RegistryContext db, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure<EmployeeEntity>(Error.NotFound("Employee.NotFound", "Colaborador não encontrado.")).ToOk();
            employee.Activate();
            await db.SaveChangesAsync(ct);
            return Result.Success(employee).ToOk();
        }).WithName("ActivateEmployee");

        group.MapDelete("/{id:guid}", async (Guid id, RegistryContext db, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Result.Failure(Error.NotFound("Employee.NotFound", "Colaborador não encontrado.")).ToOk();
            employee.MarkDeleted(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Result.Success().ToOk();
        }).WithName("DeleteEmployee");
    }
}
