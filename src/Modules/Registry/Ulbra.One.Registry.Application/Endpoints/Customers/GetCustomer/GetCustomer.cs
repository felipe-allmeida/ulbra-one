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

namespace Ulbra.One.Registry.Application.Endpoints.Customers.GetCustomer;

public sealed class GetCustomer : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetCustomer")
            .WithTags("Registry")
            .WithSummary("Gets a customer.")
            .WithDescription("Returns a single customer by id, including its address and every registered detail. "
                + "A customer belongs to one company.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CustomerId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var customer = await uow.ReadSet<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        return (customer is null
            ? Result.Failure<CustomerResponse>(Error.NotFound(ErrorCodes.Customer.NotFound, "Customer not found."))
            : Result.Success(CustomerResponse.From(customer))).ToOk();
    }
}
