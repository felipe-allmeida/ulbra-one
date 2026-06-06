using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Results;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.ActivateEmployee;

public sealed class ActivateEmployee : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/activate", Handle)
            .WithName("ActivateEmployee")
            .WithTags("Registry")
            .RequireAuthorization()
            .Produces<EmployeeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        EmployeeId id,
        [FromKeyedServices("registry")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var employee = await uow.Set<EmployeeEntity>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (employee is null)
            return Result.Failure<EmployeeResponse>(Error.NotFound("Employee.NotFound", "Employee not found.")).ToOk();
        employee.Activate();
        await uow.SaveChangesAsync(ct);
        return Result.Success(EmployeeResponse.From(employee)).ToOk();
    }
}
