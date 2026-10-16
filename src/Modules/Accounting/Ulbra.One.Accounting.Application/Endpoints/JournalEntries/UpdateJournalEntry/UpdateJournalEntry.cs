using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Units;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.UpdateJournalEntry;

/// <summary>Replaces a draft entry's header and lines. Posted/Reversed entries are immutable (409).</summary>
public sealed class UpdateJournalEntry : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Accounting competence date — the date that decides which period the entry lands in.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly CompetenceDate { get; set; }

        [SwaggerSchema("What the entry records (histórico).")]
        [SwaggerExample("Provisão de despesas de manutenção — agosto/2026")]
        public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Free-text pointer back to the originating document.")]
        public string? SourceReference { get; set; }

        [SwaggerSchema("The entry's complete set of lines — this REPLACES the existing ones, it does not "
            + "append. Debits must equal credits.")]
        public IReadOnlyList<JournalLineInput> Lines { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateJournalEntry").WithTags("Accounting")
            .WithSummary("Updates a draft journal entry.")
            .WithDescription("Replaces a draft's header and its whole set of lines. Only drafts are "
                + "editable: a posted or reversed entry is immutable and returns 409 — correcting one "
                + "means reversing it and posting a new entry. The owning company is immutable.")
            .RequirePermission(Permissions.AccountingWrite)
            .Produces<JournalEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(JournalEntryId id, Request req, IUnitOfWork uow, ICostCenterLookup costCenters, IUnitLookup units, CancellationToken ct)
    {
        var entry = await uow.Set<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (entry is null) return ErrorResults.NotFound("Journal entry not found.", ErrorCodes.JournalEntry.NotFound);
        if (entry.Status != JournalStatus.Draft)
            return ErrorResults.InvalidState("Only a draft entry can be modified.", ErrorCodes.JournalEntry.NotDraft);
        if (string.IsNullOrWhiteSpace(req.Description))
            return ErrorResults.InvalidRequest("Description is required.", ErrorCodes.JournalEntry.Invalid);

        // Structural invariants are owned by the domain (single source of truth); account I/O checks
        // are validated separately.
        var lineProblem = JournalLineValidation.TryBuildLines(req.Lines, out var lines);
        if (lineProblem is not null) return lineProblem;

        entry.UpdateHeader(req.CompetenceDate, req.Description, req.SourceReference);
        // Replace the draft's lines. New lines carry a client-generated, non-default key, so EF would
        // infer Modified (→ 0-row UPDATE) when they are reached via navigation fixup; add them
        // explicitly so they are tracked as Added (INSERT).
        foreach (var existing in entry.Lines.ToList())
            entry.RemoveLine(existing.Id);
        foreach (var line in lines)
        {
            entry.AddLine(line);
            await uow.AddAsync(line, ct);
        }

        var structuralProblem = JournalLineValidation.ValidateDraftComposition(entry);
        if (structuralProblem is not null) return structuralProblem;
        var problem = await JournalLineValidation.ValidateAsync(req.Lines, uow, ct);
        if (problem is not null) return problem;
        var costCenterProblem = await JournalLineValidation.ValidateCostCentersAsync(req.Lines, entry.CompanyId, costCenters, ct);
        if (costCenterProblem is not null) return costCenterProblem;
        var unitProblem = await JournalLineValidation.ValidateUnitsAsync(req.Lines, entry.CompanyId, units, ct);
        if (unitProblem is not null) return unitProblem;

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(JournalEntryResponse.From(entry));
    }
}
