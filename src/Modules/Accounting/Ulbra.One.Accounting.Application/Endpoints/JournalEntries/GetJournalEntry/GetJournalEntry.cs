using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.GetJournalEntry;

public sealed class GetJournalEntry : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetJournalEntry").WithTags("Accounting")
            .RequireAuthorization().Produces<JournalEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(JournalEntryId id, IUnitOfWork uow, CancellationToken ct)
    {
        var entry = await uow.ReadSet<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        return entry is null
            ? TypedResults.Problem(detail: "Journal entry not found.", statusCode: 404, title: "JournalEntry.NotFound")
            : TypedResults.Ok(JournalEntryResponse.From(entry));
    }
}
