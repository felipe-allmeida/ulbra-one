using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.ListTransactions;

/// <summary>Query-string filters for the legacy transaction list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListTransactionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the transactions. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Keep only outgoing or only incoming transactions.")]
    public TransactionType? Type { get; set; }

    [SwaggerSchema("Lifecycle stage to keep.")]
    public TransactionStatus? Status { get; set; }

    [SwaggerSchema("Cost center the transaction is charged to.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Vendor counterparty, for outgoing transactions.")]
    public VendorId? VendorId { get; set; }

    [SwaggerSchema("Customer counterparty, for incoming transactions.")]
    public CustomerId? CustomerId { get; set; }
}

public sealed class ListTransactions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListTransactions")
            .WithSummary("Lists legacy transactions.")
            .WithDescription("LEGACY. Transaction is the old generic finance entity, superseded by the Payable aggregate - "
                + "new work goes through /api/payables. Kept for the records created before the migration.")
            .WithTags("Finance")
            .RequirePermission(Permissions.TreasuryRead)
            .Produces<PaginatedResponse<TransactionResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListTransactionsRequest req,
        CancellationToken ct)
    {
        var (companyId, type, status, costCenterId, vendorId, customerId) =
            (req.CompanyId, req.Type, req.Status, req.CostCenterId, req.VendorId, req.CustomerId);
        var q = uow.ReadSet<Transaction>();
        if (companyId is { } cid) q = q.Where(l => l.CompanyId == cid.Value);
        if (type is not null) q = q.Where(l => l.Type == type);
        if (status is not null) q = q.Where(l => l.Status == status);
        if (costCenterId is { } ccid) q = q.Where(l => l.CostCenterId == ccid.Value);
        if (vendorId is { } vid) q = q.Where(l => l.VendorId == vid.Value);
        if (customerId is { } custid) q = q.Where(l => l.CustomerId == custid.Value);
        var page = await q.OrderBy(l => l.DueDate).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(TransactionResponse.From));
    }
}
