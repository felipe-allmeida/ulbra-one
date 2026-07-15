using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetJournalBook;

public sealed record GetJournalBookRequest
{
    public CompanyId? CompanyId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

/// <summary>Livro Diário (read-only): scriptured entries (Posted/Reversed) in a competence range, grouped by
/// day with daily and grand totals, ordered chronologically. Drafts are excluded.</summary>
public sealed class GetJournalBook : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/journal-book", Handle).WithName("GetJournalBook").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingRead).Produces<JournalBookResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] GetJournalBookRequest req, CancellationToken ct)
    {
        var rows = await JournalBookLoader.LoadAsync(uow, req.CompanyId?.Value, req.From, req.To, ct);
        var book = JournalBookBuilder.Build(rows, req.From, req.To);
        return TypedResults.Ok(JournalBookResponse.Of(book, req.CompanyId));
    }
}
