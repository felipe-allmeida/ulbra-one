using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ListRemittanceReturns;

/// <summary>Pagination for the return history. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListRemittanceReturnsRequest : PaginationQueryParameters;

/// <summary>
/// <c>GET /remittances/return/history/{payingBankAccountId}</c> — the CNAB returns already processed for
/// one paying account, newest first. Each row points at a stored .RET, downloadable through
/// <c>DownloadRemittanceReturnFile</c>.
/// </summary>
public sealed class ListRemittanceReturns : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/return/history/{payingBankAccountId}", Handle)
            .WithName("ListRemittanceReturns").WithTags("Finance")
            .WithSummary("Lists the bank returns already processed for a paying account.")
            .WithDescription("Every CNAB return uploaded for this account, newest first, with who processed it, when, "
                + "and what it did - settled, rejected, unmatched and left-for-review counts. A row flagged "
                + "as not applied is a file whose trailer did not reconcile: nothing was settled, but the "
                + "file was kept so it can still be downloaded and taken back to the bank.").RequirePermission(Permissions.TreasuryRead)
            .Produces<PaginatedResponse<RemittanceReturnHistoryItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Paying account whose return history to list.")]
        BankAccountId payingBankAccountId,
        UserContext user,
        IUnitOfWork uow,
        [AsParameters] ListRemittanceReturnsRequest req,
        CancellationToken ct)
    {
        // Company scope: the account must exist and be accessible to the caller (AEL-764 pattern).
        var bankAccount = await uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == payingBankAccountId.Value, ct);
        if (bankAccount is null || !user.CanAccess(bankAccount.CompanyId))
            return ErrorResults.NotFound("Bank account not found.", ErrorCodes.BankAccount.NotFound);

        var page = await uow.ReadSet<RemittanceReturn>()
            .Where(r => r.PayingBankAccountId == payingBankAccountId.Value)
            .OrderByDescending(r => r.ProcessedAt)
            .ToPaginatedAsync(req, ct);

        return TypedResults.Ok(page.Map(RemittanceReturnHistoryItem.From));
    }
}
