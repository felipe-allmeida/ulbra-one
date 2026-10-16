using Ulbra.One.FirestoreImport.Source;

namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Maps a legacy `adequacoes` document onto the One's BudgetAdjustment. Adequações are imported as
/// HISTORY only — they NEVER mutate the budget ceiling (in the One that happens in the approve/reverse
/// endpoints, not in the DB, and the imported `orcamentos` already carry the final value). Type/status are
/// enum-name strings. A "Revisão Orçamentária" carries a per-competency map (`competencyValues`) and fans out
/// to one row per month, its direction taken from `revisionAction`; the other types are a single row on the
/// doc's competency (Transferência also carries a source competency).</summary>
public static class AdjustmentMapping
{
    public static string MapStatus(string? legacy) => (legacy ?? string.Empty).Trim() switch
    {
        "Aprovado" => "Approved",
        "Cancelado" => "Cancelled",
        "Aguardando_Administrador" => "AwaitingApproval",
        "Complementação" => "AwaitingTriage", // rework → back to the initial (triage) state
        _ => "AwaitingTriage",
    };

    // The One type per fanned-out row. Suplementação/Redução/Transferência map straight; "Revisão
    // Orçamentária" has no direction of its own — it comes from revisionAction (Aumento/Diminuição).
    public static string MapType(string? legacyType, string? revisionAction) =>
        (legacyType ?? string.Empty).Trim() switch
        {
            "Suplementação" => "Supplementation",
            "Redução" => "Reduction",
            "Transferência" => "Transfer",
            "Revisão Orçamentária" => RevisionDirection(revisionAction),
            _ => "Supplementation",
        };

    private static string RevisionDirection(string? action) => (action ?? string.Empty).Trim() switch
    {
        "Diminuição" => "Reduction",
        _ => "Supplementation", // Aumento / unknown
    };

    private static bool IsRevision(string? legacyType)
        => string.Equals((legacyType ?? string.Empty).Trim(), "Revisão Orçamentária", StringComparison.Ordinal);

    /// <summary>Fans a doc out to its target rows: one per competency for a Revisão (from
    /// <c>competencyValues</c>, invalid competencies dropped), otherwise a single row on the doc's competency.
    /// Each entry carries the One type, the competency, the value and — for Transfer — the source competency.</summary>
    public static IReadOnlyList<AdjustmentEntry> ResolveEntries(AdjustmentDoc d)
    {
        if (IsRevision(d.Type))
        {
            var type = RevisionDirection(d.RevisionAction);
            return [.. d.CompetencyValues
                .Where(kv => CompetencyRange.IsValid(kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new AdjustmentEntry(type, kv.Key.Trim(), null, kv.Value))];
        }

        var oneType = MapType(d.Type, d.RevisionAction);
        var source = oneType == "Transfer" && CompetencyRange.IsValid(d.SourceCompetency)
            ? d.SourceCompetency!.Trim()
            : null;
        return [new AdjustmentEntry(oneType, (d.Competency ?? string.Empty).Trim(), source, d.Value ?? 0m)];
    }

    /// <summary>The synthetic status-transition trail for the timeline, derived from the final status and the
    /// real timestamps. AwaitingTriage (Complementação) has no transition — it IS the initial state.</summary>
    public static IReadOnlyList<TransitionSpec> BuildTransitions(
        string oneStatus, DateTimeOffset? createdAt, DateTimeOffset? updatedAt,
        DateTimeOffset? cancelledAt, string? cancelReason) => oneStatus switch
    {
        "Approved" =>
        [
            new("SendToApproval", "AwaitingTriage", "AwaitingApproval", createdAt, null),
            new("Approve", "AwaitingApproval", "Approved", updatedAt, null),
        ],
        "Cancelled" =>
        [
            new("Cancel", "AwaitingTriage", "Cancelled", cancelledAt ?? updatedAt, cancelReason),
        ],
        "AwaitingApproval" =>
        [
            new("SendToApproval", "AwaitingTriage", "AwaitingApproval", updatedAt, null),
        ],
        _ => [], // AwaitingTriage — no transition
    };
}

/// <summary>One target row of a fanned-out adequação: the One type, the competency it lands on, the value,
/// and (Transfer only) the source competency.</summary>
public sealed record AdjustmentEntry(string Type, string Competency, string? SourceCompetency, decimal Value);

/// <summary>One synthetic transition of the adequação timeline (a `budget_adjustment_transition` row).</summary>
public sealed record TransitionSpec(
    string Action, string FromStatus, string ToStatus, DateTimeOffset? OccurredAt, string? Reason);
