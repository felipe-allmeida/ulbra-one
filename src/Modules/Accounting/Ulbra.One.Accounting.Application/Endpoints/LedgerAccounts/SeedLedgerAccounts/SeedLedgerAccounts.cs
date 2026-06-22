using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Seeding;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.SeedLedgerAccounts;

/// <summary>Seeds the standard Brazilian chart-of-accounts template for a company. Idempotent:
/// a no-op (returns the existing chart) when the company already has ledger accounts.</summary>
public sealed class SeedLedgerAccounts : IEndpoint
{
    public sealed record Request(CompanyId CompanyId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/seed", Handle).WithName("SeedLedgerAccounts").WithTags("Accounting")
            .RequireAuthorization()
            .Produces<List<LedgerAccountResponse>>(StatusCodes.Status201Created)
            .Produces<List<LedgerAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;
        if (companyId == Guid.Empty)
            return TypedResults.Problem(detail: "CompanyId is required.", statusCode: 400, title: "LedgerAccount.Invalid");

        var existing = await uow.ReadSet<LedgerAccount>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);
        if (existing.Count > 0)
            return TypedResults.Ok(existing.Select(LedgerAccountResponse.From).ToList());

        var accounts = BrazilianChartTemplate.Build(companyId);
        foreach (var account in accounts)
            await uow.AddAsync(account, ct);
        await uow.SaveChangesAsync(ct);

        var body = accounts.OrderBy(a => a.Code).Select(LedgerAccountResponse.From).ToList();
        return TypedResults.Created($"/api/ledger-accounts?companyId={req.CompanyId}", body);
    }
}
