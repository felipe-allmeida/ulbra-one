using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ListJournalEntries;

/// <summary>Query-string filters for the journal entry list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListJournalEntriesRequest
{
    [SwaggerSchema("Company that owns the entries. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Lifecycle stage to keep: draft, posted or reversed.")]
    public JournalStatus? Status { get; set; }

    [SwaggerSchema("Keep entries whose competence date is on or after this day.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }

    [SwaggerSchema("Keep entries whose competence date is on or before this day.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }
}

public sealed class ListJournalEntries : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListJournalEntries").WithTags("Accounting")
            .WithSummary("Lists journal entries.")
            .WithDescription("Journal entries filtered by company, status and competence range. The dates "
                + "filter on COMPETENCE, not on when the entry was created. Drafts are included unless "
                + "status narrows them out. A reference list — no paging.")
            .RequirePermission(Permissions.AccountingRead).Produces<List<JournalEntryResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] ListJournalEntriesRequest req, CancellationToken ct)
    {
        var q = uow.ReadSet<JournalEntry>();
        if (req.CompanyId is { } cid) q = q.Where(e => e.CompanyId == cid.Value);
        if (req.Status is { } st) q = q.Where(e => e.Status == st);
        if (req.From is { } from) q = q.Where(e => e.CompetenceDate >= from);
        if (req.To is { } to) q = q.Where(e => e.CompetenceDate <= to);

        var rows = await q
            .OrderBy(e => e.CompetenceDate).ThenBy(e => e.Number)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(JournalEntryResponse.From).ToList());
    }
}
