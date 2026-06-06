using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.ListTransactions;

public sealed class ListTransactions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListTransactions")
            .WithTags("Finance")
            .RequireAuthorization()
            .Produces<PaginatedResponse<TransactionResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CompanyId? companyId,
        TransactionType? type,
        TransactionStatus? status,
        CostCenterId? costCenterId,
        VendorId? vendorId,
        CustomerId? customerId,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var q = uow.ReadSet<Transaction>();
        if (companyId is { } cid) q = q.Where(l => l.CompanyId == cid.Value);
        if (type is not null) q = q.Where(l => l.Type == type);
        if (status is not null) q = q.Where(l => l.Status == status);
        if (costCenterId is { } ccid) q = q.Where(l => l.CostCenterId == ccid.Value);
        if (vendorId is { } vid) q = q.Where(l => l.VendorId == vid.Value);
        if (customerId is { } custid) q = q.Where(l => l.CustomerId == custid.Value);
        var page = await q.OrderBy(l => l.DueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(TransactionResponse.From));
    }
}
