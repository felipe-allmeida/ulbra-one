using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.UpdateEmployee;

public sealed class UpdateEmployee : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateEmployee")
            .WithTags("Registry")
            .RequireAuthorization()
            .WithRequestNormalizer<EmployeeRequest>()
            .WithValidation<EmployeeRequest>()
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        EmployeeId id,
        EmployeeRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var employee = await uow.Set<EmployeeEntity>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (employee is null)
            return Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
        employee.Update(req.Name, req.Document, req.Email, req.Phone, req.Role,
            req.UnitId.Value, req.CostCenterId?.Value, req.HireDate, req.Address.ToAddress());
        await uow.SaveChangesAsync(ct);
        return Result.Success(EmployeeResponse.From(employee)).ToOk();
    }
}
