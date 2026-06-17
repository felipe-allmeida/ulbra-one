using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ListStatementImports;

public sealed class ListStatementImports : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListStatementImports").WithTags("Finance")
            .RequireAuthorization().Produces<List<StatementImportResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        CompanyId? companyId, BankAccountId? bankAccountId, CancellationToken ct)
    {
        var q = uow.ReadSet<BankStatementImport>();
        if (companyId is { } c) q = q.Where(i => i.CompanyId == c.Value);
        if (bankAccountId is { } b) q = q.Where(i => i.BankAccountId == b.Value);
        var rows = await q.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(StatementImportResponse.From).ToList());
    }
}
