using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ListBankTransactions;

public sealed class ListBankTransactions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListBankTransactions").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<BankTransactionResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CompanyId? companyId, BankAccountId? bankAccountId, StatementImportId? importId, MatchStatus? matchStatus,
        DateOnly? dueFrom, DateOnly? dueTo, [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        var q = uow.ReadSet<BankTransaction>();
        if (companyId is { } c) q = q.Where(t => t.CompanyId == c.Value);
        if (bankAccountId is { } b) q = q.Where(t => t.BankAccountId == b.Value);
        if (importId is { } im) q = q.Where(t => t.ImportId == im.Value);
        if (matchStatus is { } ms) q = q.Where(t => t.MatchStatus == ms);
        if (dueFrom is { } from) q = q.Where(t => t.Date >= from);
        if (dueTo is { } to) q = q.Where(t => t.Date <= to);
        var page = await q.OrderBy(t => t.Date).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(BankTransactionResponse.From));
    }
}
