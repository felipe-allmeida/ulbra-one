using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.PostJournalEntry;

/// <summary>Posts a draft entry: computes the next per-company sequential Number = max(Number)+1, then
/// freezes the entry. Concurrency is guarded by the unique (company, number) index — a racing post
/// fails the unique constraint, so the handler re-fetches a fresh draft, recomputes the max and retries.</summary>
public sealed class PostJournalEntry : IEndpoint
{
    private const int MaxAttempts = 5;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/post", Handle).WithName("PostJournalEntry").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingPost)
            .Produces<JournalEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(JournalEntryId id, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var entry = await uow.Set<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
            if (entry is null) return TypedResults.Problem(detail: "Journal entry not found.", statusCode: 404, title: "JournalEntry.NotFound");
            if (entry.Status != JournalStatus.Draft)
                return TypedResults.Problem(detail: "Only a draft entry can be posted.", statusCode: 409, title: "JournalEntry.NotDraft");

            var maxNumber = await uow.ReadSet<JournalEntry>()
                .Where(e => e.CompanyId == entry.CompanyId && e.Number != null)
                .MaxAsync(e => (int?)e.Number, ct) ?? 0;

            try { entry.Post(maxNumber + 1, clock.GetUtcNow()); }
            catch (JournalEntryException ex)
            {
                // 1:1 mapping: the domain carries a distinct stable code per structural rule
                // (TooFewLines / NonPositiveAmount / Unbalanced).
                return TypedResults.Problem(detail: ex.Error.Description, statusCode: StatusCodes.Status400BadRequest, title: ex.Error.Code);
            }

            try
            {
                await uow.SaveChangesAsync(ct);
                return TypedResults.Ok(JournalEntryResponse.From(entry));
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Lost the race for this Number against a concurrent post. The tracked entry was
                // mutated in-memory; clearing the tracker lets the next iteration re-fetch a fresh
                // draft and recompute the max. (Manual posting makes this contention rare.)
                uow.ClearChangeTracker();
            }
        }
    }
}
