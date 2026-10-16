using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Periods;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ReverseJournalEntry;

/// <summary>Reverses a posted entry, producing a new balanced Posted entry with mirrored lines.
/// The reversal entry is returned. Only Posted entries can be reversed.</summary>
public sealed class ReverseJournalEntry : IEndpoint
{
    private const int MaxAttempts = 5;

    public sealed record Request
    {
        [SwaggerSchema("Why the entry is being reversed. It becomes the reversal entry's description, so "
            + "it is what an auditor reads to understand the correction.")]
        [SwaggerExample("Estorno — lançamento em centro de custo incorreto")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Competence of the reversal; defaults to the original entry's competence when omitted.</summary>
        [SwaggerSchema("Competence date of the REVERSAL entry. Omit to reuse the original's competence; "
            + "set a later one to book the correction in an open period when the original's is closed.")]
        [SwaggerExample("2026-09-30")]
        public DateOnly? CompetenceDate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reverse", Handle).WithName("ReverseJournalEntry").WithTags("Accounting")
            .WithSummary("Reverses a posted journal entry.")
            .WithDescription("Creates a NEW posted entry with the original's lines mirrored, and returns "
                + "that reversal — the original stays untouched, because a posted entry is immutable. Only "
                + "posted entries can be reversed; a draft or an already-reversed entry returns 409.")
            .RequirePermission(Permissions.AccountingPost)
            .Produces<JournalEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(JournalEntryId id, Request req, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return ErrorResults.InvalidRequest("A reversal reason is required.", ErrorCodes.JournalEntry.ReasonRequired);

        for (var attempt = 0; ; attempt++)
        {
            var entry = await uow.Set<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
            if (entry is null) return ErrorResults.NotFound("Journal entry not found.", ErrorCodes.JournalEntry.NotFound);
            if (entry.Status != JournalStatus.Posted)
                return ErrorResults.InvalidState("Only a posted entry can be reversed.", ErrorCodes.JournalEntry.NotPosted);

            var competenceDate = req.CompetenceDate ?? entry.CompetenceDate;

            // Closed-period guard: the reversal lands in this competence — block if that period is closed
            // (reversing an entry from a closed period requires supplying an open competence).
            var periodProblem = await AccountingPeriodGuard.EnsureOpenAsync(uow, entry.CompanyId, competenceDate, ct);
            if (periodProblem is not null) return periodProblem;

            var maxNumber = await uow.ReadSet<JournalEntry>()
                .Where(e => e.CompanyId == entry.CompanyId && e.Number != null)
                .MaxAsync(e => (int?)e.Number, ct) ?? 0;

            JournalEntry reversal;
            try { reversal = entry.Reverse(req.Reason, competenceDate, maxNumber + 1, clock.GetUtcNow()); }
            catch (JournalEntryException ex)
            {
                // A posted entry mirrored into a reversal re-validates structurally; surface the
                // distinct stable code 1:1 (same codes as Post).
                return ErrorResults.InvalidRequest(ex.Error.Description, ex.Error.Code);
            }
            catch (ArgumentException ex) { return ErrorResults.InvalidRequest(ex.Message, ErrorCodes.JournalEntry.Unreversible); }

            await uow.AddAsync(reversal, ct);
            try
            {
                await uow.SaveChangesAsync(ct);
                return TypedResults.Created($"/api/journal-entries/{new JournalEntryId(reversal.Id)}", JournalEntryResponse.From(reversal));
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                uow.ClearChangeTracker();
            }
        }
    }
}
