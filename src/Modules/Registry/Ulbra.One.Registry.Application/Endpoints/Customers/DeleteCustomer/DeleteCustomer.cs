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
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Endpoints.Customers.DeleteCustomer;

public sealed class DeleteCustomer : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteCustomer")
            .WithTags("Registry")
            .WithSummary("Deletes a customer.")
            .WithDescription("Soft-deletes the customer: the row is kept and stamped, so documents that already "
                + "reference it still resolve the name, and it simply stops appearing in the list. To "
                + "take a customer out of use while keeping the record visible, deactivate it instead.")
            .RequirePermission(Permissions.RegistryWrite)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CustomerId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var customer = await uow.Set<CustomerEntity>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (customer is null)
            return Result.Failure(Error.NotFound(ErrorCodes.Customer.NotFound, "Customer not found.")).ToOk();
        customer.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return Result.Success().ToOk();
    }
}
