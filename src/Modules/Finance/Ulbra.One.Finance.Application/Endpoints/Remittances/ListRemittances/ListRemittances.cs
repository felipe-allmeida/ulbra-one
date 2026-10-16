using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ListRemittances;

/// <summary>Query-string filters for the remittance list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListRemittancesRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the paying account. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Paying bank account the remittances were generated for.")]
    public BankAccountId? PayingBankAccountId { get; set; }

    [SwaggerSchema("Keep only remittances in this state (generated, cancelled, confirmed, partially returned).")]
    public RemittanceStatus? Status { get; set; }

    [SwaggerSchema("Keep only remittances of this payment form (Pix/Ted/BoletoBancario/Convenio).")]
    public RemittanceCategory? Category { get; set; }

    [SwaggerSchema("Keep remittances generated on or after this day.")]
    [SwaggerExample("2026-08-01")]
    public DateOnly? From { get; set; }

    [SwaggerSchema("Keep remittances generated on or before this day.")]
    [SwaggerExample("2026-08-31")]
    public DateOnly? To { get; set; }
}

public sealed class ListRemittances : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle)
            .WithName("ListRemittances").WithTags("Finance")
            .WithSummary("Lists payment remittances.")
            .WithDescription("The remittances generated for the bank, newest first, with the state of each.").RequirePermission(Permissions.TreasuryRead)
            .Produces<PaginatedResponse<RemittanceListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListRemittancesRequest req,
        CancellationToken ct)
    {
        var (companyId, payingBankAccountId) = (req.CompanyId, req.PayingBankAccountId);
        var query = uow.ReadSet<Remittance>();
        if (companyId is { } cid) query = query.Where(r => r.CompanyId == cid.Value);
        if (payingBankAccountId is { } bid) query = query.Where(r => r.PayingBankAccountId == bid.Value);
        if (req.Status is { } st) query = query.Where(r => r.Status == st);
        if (req.Category is { } cat) query = query.Where(r => r.Category == cat);
        if (req.From is { } from)
        {
            var f = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.GeneratedAt >= f);
        }
        if (req.To is { } to)
        {
            var t = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.GeneratedAt < t);
        }

        var page = await query
            .OrderByDescending(r => r.GeneratedAt)
            .ToPaginatedAsync(req, ct);

        return TypedResults.Ok(page.Map(RemittanceListItemResponse.From));
    }
}
