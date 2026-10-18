namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>One remittance this title was sent in — the step between scheduling and the bank paying, which the
/// timeline used to skip entirely (the title went from "aprovado" straight to "pagamento registrado").</summary>
public sealed record RemittanceTrailEntry(
    DateTimeOffset GeneratedAt, string GeneratedBy, string FileName, string BankCode, decimal Amount);

/// <summary>One bank-statement line tied to a payment of this title — the last step of the flow, and the proof
/// the money actually left the account. <paramref name="Linked"/> distinguishes a line the conciliation merely
/// linked to an existing baixa from one whose confirmation registered the payment.</summary>
public sealed record ConciliationTrailEntry(DateTimeOffset At, DateOnly Date, decimal Amount, bool Linked);

/// <summary>
/// Context the timeline needs but the <c>Payable</c> aggregate does not carry: data owned by other modules
/// (the requisition that originated it, the ledger account it was classified to) and sibling Finance
/// aggregates (the remittances it travelled in). Resolved by the endpoint and passed in, so the builder stays
/// a pure function over its inputs.
/// </summary>
public sealed record PayableTrailContext(
    DateTimeOffset? RequisitionCreatedAt = null,
    string? RequisitionCreatedBy = null,
    decimal? AutoApprovalLimit = null,
    string? ClassifiedAccountCode = null,
    string? ClassifiedAccountName = null,
    IReadOnlyList<RemittanceTrailEntry>? Remittances = null,
    IReadOnlyList<ConciliationTrailEntry>? Conciliations = null)
{
    public static readonly PayableTrailContext Empty = new();

    public IReadOnlyList<RemittanceTrailEntry> RemittanceList => Remittances ?? [];

    public IReadOnlyList<ConciliationTrailEntry> ConciliationList => Conciliations ?? [];
}
