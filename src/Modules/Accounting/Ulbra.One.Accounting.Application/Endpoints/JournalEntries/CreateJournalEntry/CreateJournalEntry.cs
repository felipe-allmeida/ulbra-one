using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Units;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries;
using Ulbra.One.Accounting.Application.Periods;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries.CreateJournalEntry;

public sealed class CreateJournalEntry : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company the entry belongs to. Its accounting period must be open.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Accounting competence date — the date that decides which period the entry lands "
            + "in, not when it was typed.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly CompetenceDate { get; set; }

        [SwaggerSchema("What the entry records (histórico).")]
        [SwaggerExample("Provisão de despesas de manutenção — agosto/2026")]
        public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Free-text pointer back to the originating document, for tracing an entry to what "
            + "produced it.")]
        [SwaggerExample("payable_3fa85f64571742199730000000000000")]
        public string? SourceReference { get; set; }

        [SwaggerSchema("The entry's lines. Debits must equal credits; an unbalanced entry is rejected.")]
        public IReadOnlyList<JournalLineInput> Lines { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateJournalEntry").WithTags("Accounting")
            .WithSummary("Creates a draft journal entry.")
            .WithDescription("Records a manual entry as a DRAFT — it does not hit the ledger until it is "
                + "posted. Debits must equal credits, every line must target a posting account, and any "
                + "cost center or unit on a line is validated against the company. The entry gets no "
                + "number yet: numbering happens at posting.")
            .RequirePermission(Permissions.AccountingWrite)
            .Produces<JournalEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, ICostCenterLookup costCenters, IUnitLookup units, CancellationToken ct)
    {
        if (req.CompanyId.Value == Guid.Empty)
            return ErrorResults.InvalidRequest("CompanyId is required.", ErrorCodes.JournalEntry.Invalid);
        if (string.IsNullOrWhiteSpace(req.Description))
            return ErrorResults.InvalidRequest("Description is required.", ErrorCodes.JournalEntry.Invalid);

        // Closed-period guard: block creating a draft whose competence falls in a closed period.
        var periodProblem = await AccountingPeriodGuard.EnsureOpenAsync(uow, req.CompanyId.Value, req.CompetenceDate, ct);
        if (periodProblem is not null) return periodProblem;

        // Structural invariants are owned by the domain (single source of truth); account I/O checks
        // are validated separately.
        var lineProblem = JournalLineValidation.TryBuildLines(req.Lines, out var lines);
        if (lineProblem is not null) return lineProblem;

        var entry = new JournalEntry(req.CompanyId.Value, req.CompetenceDate, req.Description,
            sourceReference: req.SourceReference);
        foreach (var line in lines) entry.AddLine(line);

        var structuralProblem = JournalLineValidation.ValidateDraftComposition(entry);
        if (structuralProblem is not null) return structuralProblem;
        var problem = await JournalLineValidation.ValidateAsync(req.Lines, uow, ct);
        if (problem is not null) return problem;
        var costCenterProblem = await JournalLineValidation.ValidateCostCentersAsync(req.Lines, req.CompanyId.Value, costCenters, ct);
        if (costCenterProblem is not null) return costCenterProblem;
        var unitProblem = await JournalLineValidation.ValidateUnitsAsync(req.Lines, req.CompanyId.Value, units, ct);
        if (unitProblem is not null) return unitProblem;

        await uow.AddAsync(entry, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/journal-entries/{new JournalEntryId(entry.Id)}", JournalEntryResponse.From(entry));
    }
}
