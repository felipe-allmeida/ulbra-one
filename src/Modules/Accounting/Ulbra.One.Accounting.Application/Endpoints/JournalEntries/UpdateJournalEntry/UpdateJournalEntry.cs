using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.UpdateJournalEntry;

/// <summary>Replaces a draft entry's header and lines. Posted/Reversed entries are immutable (409).</summary>
public sealed class UpdateJournalEntry : IEndpoint
{
    public sealed record Request
    {
        public DateOnly CompetenceDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? SourceReference { get; set; }
        public IReadOnlyList<JournalLineInput> Lines { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdateJournalEntry").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingWrite)
            .Produces<JournalEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(JournalEntryId id, Request req, IUnitOfWork uow, ICostCenterLookup costCenters, CancellationToken ct)
    {
        var entry = await uow.Set<JournalEntry>().FirstOrDefaultAsync(e => e.Id == id.Value, ct);
        if (entry is null) return TypedResults.Problem(detail: "Journal entry not found.", statusCode: 404, title: "JournalEntry.NotFound");
        if (entry.Status != JournalStatus.Draft)
            return TypedResults.Problem(detail: "Only a draft entry can be modified.", statusCode: 409, title: "JournalEntry.NotDraft");
        if (string.IsNullOrWhiteSpace(req.Description))
            return TypedResults.Problem(detail: "Description is required.", statusCode: 400, title: "JournalEntry.Invalid");

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

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(JournalEntryResponse.From(entry));
    }
}
