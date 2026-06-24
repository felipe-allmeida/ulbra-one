using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ListJournalEntries;

public sealed record ListJournalEntriesRequest
{
    public CompanyId? CompanyId { get; set; }
    public JournalStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class ListJournalEntries : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListJournalEntries").WithTags("Accounting")
            .RequireAuthorization().Produces<List<JournalEntryResponse>>(StatusCodes.Status200OK);

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
