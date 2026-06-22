using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.CreateJournalEntry;

public sealed class CreateJournalEntry : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        public DateOnly CompetenceDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? SourceReference { get; set; }
        public IReadOnlyList<JournalLineInput> Lines { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateJournalEntry").WithTags("Accounting")
            .RequireAuthorization()
            .Produces<JournalEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (req.CompanyId.Value == Guid.Empty)
            return TypedResults.Problem(detail: "CompanyId is required.", statusCode: 400, title: "JournalEntry.Invalid");
        if (string.IsNullOrWhiteSpace(req.Description))
            return TypedResults.Problem(detail: "Description is required.", statusCode: 400, title: "JournalEntry.Invalid");

        // Structural invariants are owned by the domain (single source of truth); account I/O checks
        // are validated separately.
        var lineProblem = JournalLineValidation.TryBuildLines(req.Lines, out var lines);
        if (lineProblem is not null) return lineProblem;

        var entry = new JournalEntry(req.CompanyId.Value, req.CompetenceDate, req.Description,
            sourceReference: req.SourceReference);
        foreach (var line in lines) entry.AddLine(line);

        var structuralProblem = JournalLineValidation.ValidateDraftComposition(entry);
        if (structuralProblem is not null) return structuralProblem;
        var problem = await JournalLineValidation.ValidateAsync(req.CompanyId.Value, req.Lines, uow, ct);
        if (problem is not null) return problem;

        await uow.AddAsync(entry, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/journal-entries/{new JournalEntryId(entry.Id)}", JournalEntryResponse.From(entry));
    }
}
