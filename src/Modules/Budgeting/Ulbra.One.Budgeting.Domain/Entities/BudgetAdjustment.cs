using Ulbra.One.Common.Domain.Auditing;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>
/// A budget adjustment (adequação orçamentária) — a request that, once approved, changes the ceiling
/// (the cell's monthly ceiling) of one or two cells: <b>Suplementação</b> raises the destination,
/// <b>Redução</b> lowers it, <b>Transferência</b> moves value from a source competency to a destination
/// competency of the same Unit × CostCenter. Two-step approval chain (mirrors the legacy Administrador →
/// Presidência): <see cref="BudgetAdjustmentStatus.AwaitingTriage"/> → <see cref="BudgetAdjustmentStatus.AwaitingApproval"/>
/// → <see cref="BudgetAdjustmentStatus.Approved"/> (moves the budget), plus <see cref="BudgetAdjustmentStatus.Reversed"/>
/// (recomposes it) and <see cref="BudgetAdjustmentStatus.Cancelled"/>. The final approval enforces the approver's
/// alçada. References company/unit/cost center by loose Guid (no cross-schema FK). The friendly
/// <see cref="Code"/> is a per-company sequential number, separate from the typed id. Mirrors
/// <see cref="Requisition"/>; the actual ceiling mutation is applied by the approve/reverse endpoints
/// (cross-aggregate), keeping this domain free of persistence.
/// </summary>
public sealed class BudgetAdjustment : IAuditable
{
    private readonly List<BudgetAdjustmentTransition> _transitions = new();

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Per-company sequential display number (friendly code), assigned at creation.</summary>
    public int Code { get; private set; }

    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid CostCenterId { get; private set; }

    public BudgetAdjustmentType Type { get; private set; }

    /// <summary>Destination competency (<c>"YYYY-MM"</c>) — the cell whose ceiling goes up (Suplementação,
    /// Transferência) or down (Redução).</summary>
    public string Competency { get; private set; } = string.Empty;

    /// <summary>Source competency (<c>"YYYY-MM"</c>); set only for <see cref="BudgetAdjustmentType.Transfer"/> —
    /// the cell the value is moved FROM. Null otherwise.</summary>
    public string? SourceCompetency { get; private set; }

    /// <summary>Amount to move. Never zero/negative; rounded to 2 decimals.</summary>
    public decimal Value { get; private set; }

    public string Justification { get; private set; } = string.Empty;

    /// <summary>Deliverables/counterparts justifying the adjustment. Required except for a Redução.</summary>
    public string? Counterparts { get; private set; }

    public BudgetAdjustmentStatus Status { get; private set; } = BudgetAdjustmentStatus.AwaitingTriage;

    /// <summary>Chronological trail of status transitions (who/when/from→to) — audit + stage SLA base.</summary>
    public IReadOnlyList<BudgetAdjustmentTransition> Transitions => _transitions;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>When the adjustment entered its CURRENT status (creation = AwaitingTriage). Base for the
    /// stage SLA on the list/detail — a scalar, so it's available without loading the transition trail.</summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    private BudgetAdjustment() { }

    public BudgetAdjustment(
        Guid companyId, Guid unitId, Guid costCenterId, int code,
        BudgetAdjustmentType type, string competency, string? sourceCompetency,
        decimal value, string justification, string? counterparts)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (unitId == Guid.Empty) throw new ArgumentException("UnitId is required.", nameof(unitId));
        if (costCenterId == Guid.Empty) throw new ArgumentException("CostCenterId is required.", nameof(costCenterId));
        if (code < 1) throw new ArgumentOutOfRangeException(nameof(code), "Code must be a positive sequential number.");

        CompanyId = companyId;
        UnitId = unitId;
        CostCenterId = costCenterId;
        Code = code;
        Status = BudgetAdjustmentStatus.AwaitingTriage;

        SetDetails(type, competency, sourceCompetency, value, justification, counterparts);
    }

    /// <summary>Updates the editable fields. Dimensions (company/unit/cost center), <see cref="Code"/> and
    /// <see cref="Status"/> are immutable. Allowed only while still awaiting triage.</summary>
    public void Update(
        BudgetAdjustmentType type, string competency, string? sourceCompetency,
        decimal value, string justification, string? counterparts)
    {
        EnsureEditable();
        SetDetails(type, competency, sourceCompetency, value, justification, counterparts);
    }

    // ---- State machine ---------------------------------------------------------------------------
    // Every transition records who/when/from→to on the aggregate (see Transitions). Contextual inputs
    // (actor, timestamp, approver limit) are passed in by the endpoint — the domain never reads
    // UserContext / TimeProvider / roles / permissions / Core types. The ceiling mutation itself is
    // applied by the endpoint (cross-aggregate) inside the same SaveChanges.

    /// <summary>Triagem forwards the adjustment to the approver.</summary>
    public void SendToApproval(Guid actorId, DateTimeOffset at) =>
        Transition(BudgetAdjustmentStatus.AwaitingApproval, BudgetAdjustmentAction.SendToApproval, actorId, at, null, BudgetAdjustmentStatus.AwaitingTriage);

    /// <summary>Final approval — moves the budget. Enforces the approver's authorization limit (alçada) as a
    /// hard domain rule with no override: blocks when <see cref="Value"/> exceeds <paramref name="approverLimit"/>.
    /// <c>null</c> = unlimited (0 blocks everything, mirroring the requisition esteira).</summary>
    public void Approve(Guid actorId, DateTimeOffset at, decimal? approverLimit)
    {
        EnsureFrom(BudgetAdjustmentStatus.AwaitingApproval);
        if (approverLimit is decimal limit && Value > limit)
            throw new BudgetAdjustmentException(BudgetAdjustmentErrors.ApprovalLimitExceeded);
        MoveTo(BudgetAdjustmentStatus.Approved, BudgetAdjustmentAction.Approve, actorId, at, null);
    }

    /// <summary>Cancels an adjustment still in the approval flow (not yet approved).</summary>
    public void Cancel(Guid actorId, DateTimeOffset at, string? reason = null) =>
        Transition(BudgetAdjustmentStatus.Cancelled, BudgetAdjustmentAction.Cancel, actorId, at, reason,
            BudgetAdjustmentStatus.AwaitingTriage, BudgetAdjustmentStatus.AwaitingApproval);

    /// <summary>Reverses an already-approved adjustment — the endpoint recomposes the ceiling by inverting
    /// the arithmetic. Immutable-once-approved: a reversal is a transition, not a hard delete.</summary>
    public void Reverse(Guid actorId, DateTimeOffset at, string? reason = null) =>
        Transition(BudgetAdjustmentStatus.Reversed, BudgetAdjustmentAction.Reverse, actorId, at, reason, BudgetAdjustmentStatus.Approved);

    /// <summary>Guards deletion: only an adjustment still awaiting triage may be hard-deleted.</summary>
    public void EnsureDeletable()
    {
        if (Status != BudgetAdjustmentStatus.AwaitingTriage)
            throw new BudgetAdjustmentException(BudgetAdjustmentErrors.NotDeletable);
    }

    // ---- Audit -----------------------------------------------------------------------------------

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; StatusChangedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }

    // ---- Helpers ---------------------------------------------------------------------------------

    private void EnsureFrom(params BudgetAdjustmentStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
            throw new BudgetAdjustmentException(BudgetAdjustmentErrors.InvalidTransition);
    }

    private void MoveTo(BudgetAdjustmentStatus target, BudgetAdjustmentAction action, Guid actorId, DateTimeOffset at, string? reason)
    {
        var from = Status;
        Status = target;
        StatusChangedAt = at; // entry into the new stage — base of the SLA
        _transitions.Add(new BudgetAdjustmentTransition(Id, from, target, action, actorId, at, reason));
    }

    private void Transition(BudgetAdjustmentStatus target, BudgetAdjustmentAction action, Guid actorId, DateTimeOffset at, string? reason, params BudgetAdjustmentStatus[] allowedFrom)
    {
        EnsureFrom(allowedFrom);
        MoveTo(target, action, actorId, at, reason);
    }

    private void EnsureEditable()
    {
        if (Status != BudgetAdjustmentStatus.AwaitingTriage)
            throw new BudgetAdjustmentException(BudgetAdjustmentErrors.NotEditable);
    }

    private void SetDetails(
        BudgetAdjustmentType type, string competency, string? sourceCompetency,
        decimal value, string justification, string? counterparts)
    {
        if (string.IsNullOrWhiteSpace(justification)) throw new ArgumentException("Justification is required.", nameof(justification));

        var dest = NormalizeCompetency(competency, nameof(competency));
        var source = NormalizeTransferSource(type, sourceCompetency, dest);

        // Counterparts (entregas) justify the extra budget — required except for a Redução (giving budget back).
        if (type != BudgetAdjustmentType.Reduction && string.IsNullOrWhiteSpace(counterparts))
            throw new ArgumentException("Counterparts are required except for a reduction.", nameof(counterparts));

        Type = type;
        Competency = dest;
        SourceCompetency = source;
        Value = NormalizeValue(value);
        Justification = justification.Trim();
        Counterparts = string.IsNullOrWhiteSpace(counterparts) ? null : counterparts.Trim();
    }

    private static decimal NormalizeValue(decimal value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than zero.");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCompetency(string competency, string paramName)
    {
        var c = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(c))
            throw new ArgumentException($"Competency '{c}' is not a valid \"YYYY-MM\" value.", paramName);
        return c;
    }

    /// <summary>A transfer requires a valid source competency distinct from the destination; any other type
    /// must not carry one. Throws the stable adequação errors so the endpoint maps them to 400/409.</summary>
    private static string? NormalizeTransferSource(BudgetAdjustmentType type, string? sourceCompetency, string destination)
    {
        if (type == BudgetAdjustmentType.Transfer)
        {
            if (string.IsNullOrWhiteSpace(sourceCompetency))
                throw new BudgetAdjustmentException(BudgetAdjustmentErrors.SourceRequired);
            var source = NormalizeCompetency(sourceCompetency, nameof(sourceCompetency));
            if (CompetencyFormat.Compare(source, destination) == 0)
                throw new BudgetAdjustmentException(BudgetAdjustmentErrors.SourceEqualsDestination);
            return source;
        }

        if (!string.IsNullOrWhiteSpace(sourceCompetency))
            throw new BudgetAdjustmentException(BudgetAdjustmentErrors.SourceNotAllowed);
        return null;
    }
}
