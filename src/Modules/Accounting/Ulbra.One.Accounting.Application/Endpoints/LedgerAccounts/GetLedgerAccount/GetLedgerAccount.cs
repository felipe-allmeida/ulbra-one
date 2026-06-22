using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.GetLedgerAccount;

public sealed class GetLedgerAccount : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetLedgerAccount").WithTags("Accounting")
            .RequireAuthorization().Produces<LedgerAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(LedgerAccountId id, IUnitOfWork uow, CancellationToken ct)
    {
        var account = await uow.ReadSet<LedgerAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        return account is null
            ? TypedResults.Problem(detail: "Ledger account not found.", statusCode: 404, title: "LedgerAccount.NotFound")
            : TypedResults.Ok(LedgerAccountResponse.From(account));
    }
}
