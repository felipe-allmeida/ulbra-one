using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Periods;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.PostJournalEntry;

/// <summary>Posts a draft entry: computes the next per-company sequential Number = max(Number)+1, then
/// freezes the entry. Concurrency is guarded by the unique (company, number) index — a racing post
/// fails the unique constraint, so the handler re-fetches a fresh draft, recomputes the max and retries.</summary>
public sealed class PostJournalEntry : IEndpoint
{
    private const int MaxAttempts = 5;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/post", Handle).WithName("PostJournalEntry").WithTags("Accounting")
            .WithSummary("Posts a draft journal entry.")
            .WithDescription("Freezes the draft and assigns it the next per-company sequential number — "
                + "this is the point at which the entry becomes part of the ledger and stops being "
                + "editable. Numbering is guarded by a unique (company, number) index, so two posts racing "
                + "for the same number resolve by retry rather than by producing a gap or a duplicate. "
                + "Posting into a closed period, or posting anything that is not a draft, returns 409.")
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
            if (entry is null) return ErrorResults.NotFound("Journal entry not found.", ErrorCodes.JournalEntry.NotFound);
            if (entry.Status != JournalStatus.Draft)
                return ErrorResults.InvalidState("Only a draft entry can be posted.", ErrorCodes.JournalEntry.NotDraft);

            // Closed-period guard: a draft created while the period was open must not post after it closed.
            var periodProblem = await AccountingPeriodGuard.EnsureOpenAsync(uow, entry.CompanyId, entry.CompetenceDate, ct);
            if (periodProblem is not null) return periodProblem;

            var maxNumber = await uow.ReadSet<JournalEntry>()
                .Where(e => e.CompanyId == entry.CompanyId && e.Number != null)
                .MaxAsync(e => (int?)e.Number, ct) ?? 0;

            try { entry.Post(maxNumber + 1, clock.GetUtcNow()); }
            catch (JournalEntryException ex)
            {
                // 1:1 mapping: the domain carries a distinct stable code per structural rule
                // (TooFewLines / NonPositiveAmount / Unbalanced).
                return ErrorResults.InvalidRequest(ex.Error.Description, ex.Error.Code);
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
