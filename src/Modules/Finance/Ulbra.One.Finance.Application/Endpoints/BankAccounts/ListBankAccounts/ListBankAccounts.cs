using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
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
            .WithSummary("Lists bank accounts.")
            .WithDescription("The company bank accounts money is paid from and received into. A reference list, no paging.")
            .RequirePermission(Permissions.TreasuryRead).Produces<List<BankAccountResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [SwaggerSchema("Company that owns the accounts. Omit to list across every company.")]
        CompanyId? companyId,
        [SwaggerSchema("Include deactivated accounts. Omitted returns only active ones.")]
        bool? includeInactive,
        CancellationToken ct)
    {
        var q = uow.ReadSet<BankAccount>();
        if (companyId is { } cid) q = q.Where(a => a.CompanyId == cid.Value);
        if (includeInactive != true) q = q.Where(a => a.Active);
        var rows = await q.OrderBy(a => a.Alias).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(BankAccountResponse.From).ToList());
    }
}
