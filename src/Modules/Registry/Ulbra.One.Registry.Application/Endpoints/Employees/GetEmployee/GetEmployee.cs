using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.GetEmployee;

public sealed class GetEmployee : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetEmployee")
            .WithTags("Registry")
            .WithSummary("Gets an employee.")
            .WithDescription("Returns a single employee by id, including its address and every registered detail. "
                + "This is registry data only — it grants no system access, which lives in Identity.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        EmployeeId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var employee = await uow.ReadSet<EmployeeEntity>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        return (employee is null
            ? Result.Failure<EmployeeResponse>(Error.NotFound(ErrorCodes.Employee.NotFound, "Employee not found."))
            : Result.Success(EmployeeResponse.From(employee))).ToOk();
    }
}
