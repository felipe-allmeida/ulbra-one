using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.ListBankAccounts;

public sealed class ListBankAccounts : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListBankAccounts").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryRead).Produces<List<BankAccountResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, CompanyId? companyId, bool? includeInactive, CancellationToken ct)
    {
        var q = uow.ReadSet<BankAccount>();
        if (companyId is { } cid) q = q.Where(a => a.CompanyId == cid.Value);
        if (includeInactive != true) q = q.Where(a => a.Active);
        var rows = await q.OrderBy(a => a.Alias).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(BankAccountResponse.From).ToList());
    }
}
