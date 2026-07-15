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

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ReverseJournalEntry;

/// <summary>Reverses a posted entry, producing a new balanced Posted entry with mirrored lines.
/// The reversal entry is returned. Only Posted entries can be reversed.</summary>
public sealed class ReverseJournalEntry : IEndpoint
{
    private const int MaxAttempts = 5;

    public sealed record Request
    {
        public string Reason { get; set; } = string.Empty;
        /// <summary>Competence of the reversal; defaults to the original entry's competence when omitted.</summary>
        public DateOnly? CompetenceDate { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reverse", Handle).WithName("ReverseJournalEntry").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingPost)
            .Produces<JournalEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(JournalEntryId id, Request req, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return TypedResults.Problem(detail: "A reversal reason is required.", statusCode: 400, title: "JournalEntry.ReasonRequired");

        for (var attempt = 0; ; attempt++)
        {
            var entry = await uow.Set<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
            if (entry is null) return TypedResults.Problem(detail: "Journal entry not found.", statusCode: 404, title: "JournalEntry.NotFound");
            if (entry.Status != JournalStatus.Posted)
                return TypedResults.Problem(detail: "Only a posted entry can be reversed.", statusCode: 409, title: "JournalEntry.NotPosted");

            var maxNumber = await uow.ReadSet<JournalEntry>()
                .Where(e => e.CompanyId == entry.CompanyId && e.Number != null)
                .MaxAsync(e => (int?)e.Number, ct) ?? 0;

            JournalEntry reversal;
            try { reversal = entry.Reverse(req.Reason, req.CompetenceDate ?? entry.CompetenceDate, maxNumber + 1, clock.GetUtcNow()); }
            catch (JournalEntryException ex)
            {
                // A posted entry mirrored into a reversal re-validates structurally; surface the
                // distinct stable code 1:1 (same codes as Post).
                return TypedResults.Problem(detail: ex.Error.Description, statusCode: StatusCodes.Status400BadRequest, title: ex.Error.Code);
            }
            catch (ArgumentException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "JournalEntry.Unreversible"); }

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
