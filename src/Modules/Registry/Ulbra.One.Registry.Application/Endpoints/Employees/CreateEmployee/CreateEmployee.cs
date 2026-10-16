using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Employees.CreateEmployee;

public sealed class CreateEmployee : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateEmployee")
            .WithTags("Registry")
            .WithSummary("Creates an employee.")
            .WithDescription("Registers an employee. The document (CNPJ or CPF) is normalized and validated with "
                + "its check digits, and the whole payload is validated before the handler runs, so a "
                + "bad field comes back as a 400 grouped by field name. This is registry data only — it "
                + "grants no system access, which lives in Identity.")
            .RequirePermission(Permissions.RegistryWrite)
            .WithRequestNormalizer<EmployeeRequest>()
            .WithValidation<EmployeeRequest>()
            .Produces<EmployeeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        EmployeeRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var employee = new EmployeeEntity(req.CompanyId.Value, req.Name, req.Document, req.Email, req.Phone,
            req.Role, req.UnitId.Value, req.CostCenterId?.Value, req.HireDate, req.Address.ToAddress());
        await uow.AddAsync(employee, ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success(EmployeeResponse.From(employee)).ToCreated(e => $"/api/employees/{e.Id}");
    }
}
