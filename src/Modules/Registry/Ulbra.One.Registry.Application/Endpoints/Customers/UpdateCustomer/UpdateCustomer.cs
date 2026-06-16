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

namespace Ulbra.One.Registry.Application.Endpoints.Customers.UpdateCustomer;

public sealed class UpdateCustomer : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateCustomer")
            .WithTags("Registry")
            .RequireAuthorization()
            .WithRequestNormalizer<CustomerRequest>()
            .WithValidation<CustomerRequest>()
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CustomerId id,
        CustomerRequest req,
        [FromKeyedServices("registry")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var customer = await uow.Set<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (customer is null)
            return Result.Failure<CustomerResponse>(Error.NotFound("Customer.NotFound", "Customer not found.")).ToOk();
        customer.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
            req.Address.ToAddress(), req.CreditLimit);
        await uow.SaveChangesAsync(ct);
        return Result.Success(CustomerResponse.From(customer)).ToOk();
    }
}
