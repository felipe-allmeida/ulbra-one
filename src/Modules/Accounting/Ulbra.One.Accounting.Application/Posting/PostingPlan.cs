using System.Text.Json;
using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Posting;

/// <summary>One planned line of a payable posting (ledger code + side + amount + optional cost center).</summary>
public sealed record PostingPlanLine(string? Code, EntrySide Side, decimal Amount, Guid? CostCenterId);

/// <summary>Serializable replay plan captured when a payable posting is parked as a
/// <see cref="Domain.Entities.PendingAccountingPosting"/>. Stores the original (pre-clamp) competence so a
/// later reprocess re-applies the closed-period clamp. Persisted as JSON on the pending row.</summary>
public sealed record PostingPlan(DateOnly CompetenceDate, string Description, IReadOnlyList<PostingPlanLine> Lines)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static PostingPlan? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PostingPlan>(json, Options);
}
