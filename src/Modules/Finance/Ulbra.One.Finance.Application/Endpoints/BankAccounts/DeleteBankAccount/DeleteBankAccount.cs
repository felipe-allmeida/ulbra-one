using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.DeleteBankAccount;

public sealed class DeleteBankAccount : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}", Handle).WithName("DeleteBankAccount").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BankAccountId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var acc = await uow.Set<BankAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (acc is null) return TypedResults.Problem(detail: "Bank account not found.", statusCode: 404, title: "BankAccount.NotFound");
        var inUse = await uow.ReadSet<Payment>().AnyAsync(p => p.BankAccountId == id.Value, ct)
            || await uow.ReadSet<Receipt>().AnyAsync(r => r.BankAccountId == id.Value, ct);
        if (inUse) return TypedResults.Problem(detail: "Bank account is in use; deactivate it instead.", statusCode: 409, title: "BankAccount.InUse");
        acc.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
