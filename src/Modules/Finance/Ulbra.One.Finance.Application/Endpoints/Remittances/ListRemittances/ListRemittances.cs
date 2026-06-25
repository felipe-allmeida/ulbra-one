using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ListRemittances;

public sealed class ListRemittances : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle)
            .WithName("ListRemittances").WithTags("Finance").RequireAuthorization()
            .Produces<PaginatedResponse<RemittanceListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        CompanyId? companyId,
        BankAccountId? payingBankAccountId,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var query = uow.ReadSet<Remittance>();
        if (companyId is { } cid) query = query.Where(r => r.CompanyId == cid.Value);
        if (payingBankAccountId is { } bid) query = query.Where(r => r.PayingBankAccountId == bid.Value);

        var page = await query
            .OrderByDescending(r => r.GeneratedAt)
            .ToPaginatedAsync(pagination, ct);

        return TypedResults.Ok(page.Map(RemittanceListItemResponse.From));
    }
}
