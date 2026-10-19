using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.DeleteBankAccount;

public sealed class DeleteBankAccount : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}", Handle).WithName("DeleteBankAccount").WithTags("Finance")
            .WithSummary("Deletes a bank account.")
            .WithDescription("Soft-deletes an account that has no movement bound to it; one already used in payments "
                + "returns 409. To stop using an account while keeping its history, deactivate it through the "
                + "update instead.")
            .RequirePermission(Permissions.TreasuryWrite).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BankAccountId id,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var acc = await uow.Set<BankAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (acc is null) return ErrorResults.NotFound("Bank account not found.", ErrorCodes.BankAccount.NotFound);
        var inUse = await uow.ReadSet<Payment>().AnyAsync(p => p.BankAccountId == id.Value, ct)
            || await uow.ReadSet<Receipt>().AnyAsync(r => r.BankAccountId == id.Value, ct);
        if (inUse) return ErrorResults.InvalidState("Bank account is in use; deactivate it instead.", ErrorCodes.BankAccount.InUse);
        acc.MarkDeleted(timeProvider.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
