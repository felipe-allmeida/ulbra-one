using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetJournalBook;

public sealed record GetJournalBookRequest
{
    [SwaggerSchema("Company to report on. Omit to consolidate across every company.")]
    public CompanyId? CompanyId { get; set; }
    [SwaggerSchema("Start of the competence range, inclusive. Filters on the entry\u0027s competence date, not "
        + "on when it was posted.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }
    [SwaggerSchema("End of the competence range, inclusive.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }
    [SwaggerSchema("Restrict to entry lines charged to this unit. Lines without one are excluded.")]
    public UnitId? UnitId { get; set; }
}

/// <summary>Livro Diário (read-only): scriptured entries (Posted/Reversed) in a competence range, grouped by
/// day with daily and grand totals, ordered chronologically. Drafts are excluded.</summary>
public sealed class GetJournalBook : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/journal-book", Handle).WithName("GetJournalBook").WithTags("Accounting")
            .WithSummary("Gets the journal book.")
            .WithDescription("Livro diario: posted entries in chronological order with their lines, as the statutory book "
                + "presents them. Numbering comes from posting, so the sequence has no gaps.")
            .RequirePermission(Permissions.AccountingRead).Produces<JournalBookResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] GetJournalBookRequest req, CancellationToken ct)
    {
        var rows = await JournalBookLoader.LoadAsync(uow, req.CompanyId?.Value, req.From, req.To, ct, req.UnitId?.Value);
        var book = JournalBookBuilder.Build(rows, req.From, req.To);
        return TypedResults.Ok(JournalBookResponse.Of(book, req.CompanyId));
    }
}
