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

namespace Ulbra.One.Registry.Application.Endpoints.Customers.CreateCustomer;

public sealed class CreateCustomer : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateCustomer")
            .WithTags("Registry")
            .WithSummary("Creates a customer.")
            .WithDescription("Registers a customer. The document (CNPJ or CPF) is normalized and validated with "
                + "its check digits, and the whole payload is validated before the handler runs, so a "
                + "bad field comes back as a 400 grouped by field name. A customer belongs to one "
                + "company.")
            .RequirePermission(Permissions.RegistryWrite)
            .WithRequestNormalizer<CustomerRequest>()
            .WithValidation<CustomerRequest>()
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        CustomerRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var customer = new CustomerEntity(req.CompanyId.Value, req.PersonType, req.Name, req.TradeName,
            req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.CreditLimit);
        await uow.AddAsync(customer, ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success(CustomerResponse.From(customer)).ToCreated(c => $"/api/customers/{c.Id}");
    }
}
