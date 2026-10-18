using System.Text.Json;
using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Posting;

/// <summary>One planned line of a payable posting (ledger code + side + amount + optional cost center + optional
/// unit/estabelecimento). <c>UnitId</c> deserializes to null on legacy plans written before the field existed.</summary>
public sealed record PostingPlanLine(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId, Guid? UnitId = null);

/// <summary>Serializable replay plan captured when a payable posting is parked as a
/// <see cref="Domain.Entities.PendingAccountingPosting"/>. Stores the original (pre-clamp) competence so a
/// later reprocess re-applies the closed-period clamp. Persisted as JSON on the pending row. <see cref="DocumentRef"/>
/// carries the document/NF number so a replayed posting keeps its human-facing SourceReference (defaults to null
/// when deserializing legacy plans written before the field existed). <see cref="CounterpartyName"/>/
/// <see cref="CounterpartyDocument"/> (razão social + CNPJ/CPF) likewise survive a replay; null on legacy plans.</summary>
public sealed record PostingPlan(
    DateOnly CompetenceDate, string Description, IReadOnlyList<PostingPlanLine> Lines, string? DocumentRef = null,
    string? CounterpartyName = null, string? CounterpartyDocument = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static PostingPlan? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PostingPlan>(json, Options);
}
