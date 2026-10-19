using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.GetJournalEntry;

public sealed class GetJournalEntry : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetJournalEntry").WithTags("Accounting")
            .WithSummary("Gets a journal entry.")
            .WithDescription("Returns a single entry by id with all of its lines.")
            .RequirePermission(Permissions.AccountingRead).Produces<JournalEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(JournalEntryId id, IUnitOfWork uow, CancellationToken ct)
    {
        var entry = await uow.ReadSet<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        return entry is null
            ? ErrorResults.NotFound("Journal entry not found.", ErrorCodes.JournalEntry.NotFound)
            : TypedResults.Ok(JournalEntryResponse.From(entry));
    }
}
