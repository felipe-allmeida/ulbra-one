using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.PendingPostings.GetPendingPostings;

public sealed record GetPendingPostingsRequest
{
    public CompanyId? CompanyId { get; set; }
}

public sealed class GetPendingPostings : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetPendingPostings").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingRead)
            .Produces<PaginatedResponse<PendingAccountingPostingResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] GetPendingPostingsRequest req,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var q = uow.ReadSet<PendingAccountingPosting>()
            .Where(p => p.ResolvedAt == null);

        if (req.CompanyId is { } cid) q = q.Where(p => p.CompanyId == cid.Value);

        var page = await q
            .OrderByDescending(p => p.CreatedAt)
            .ToPaginatedAsync(pagination, ct);

        return TypedResults.Ok(page.Map(PendingAccountingPostingResponse.From));
    }
}
