using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.ListLedgerAccounts;

public sealed class ListLedgerAccounts : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListLedgerAccounts").WithTags("Accounting")
            .WithSummary("Lists the chart of accounts.")
            .WithDescription("The global chart of accounts — one canonical plano de contas shared by every "
                + "company, so there is no company filter. A reference list, no paging.")
            .RequirePermission(Permissions.AccountingRead).Produces<List<LedgerAccountResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [SwaggerSchema("Keep only accounts of this statement group.")]
        AccountGroup? accountGroup,
        [SwaggerSchema("Keep only posting accounts or only synthetic (grouping) ones. Filter to posting "
            + "accounts to build an account picker for a journal line.")]
        LedgerAccountKind? kind,
        [SwaggerSchema("Keep only accounts that can still receive entries. Omitted returns active and "
            + "inactive alike.")]
        bool? activeOnly,
        CancellationToken ct)
    {
        var q = uow.ReadSet<LedgerAccount>();
        if (accountGroup is { } g) q = q.Where(a => a.AccountGroup == g);
        if (kind is { } k) q = q.Where(a => a.Kind == k);
        if (activeOnly == true) q = q.Where(a => a.IsActive);
        var rows = await q.OrderBy(a => a.Code).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(LedgerAccountResponse.From).ToList());
    }
}
