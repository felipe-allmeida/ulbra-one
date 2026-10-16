namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>Published (EF bus outbox) right after Finance creates a Payable sourced from a requisition (AEL-877)
/// — the return link so Budgeting can show which Payables a requisition generated. Budgeting matches on
/// (<see cref="RequisitionId"/>, <see cref="CycleCompetency"/>): a null competency is the base Payable (from
/// approval), a "YYYY-MM" is a monthly cycle. Loose Guids across the module boundary; idempotent to apply.</summary>
public sealed record PayableCreatedForRequisition(
    Guid RequisitionId, Guid PayableId, string? CycleCompetency, Guid CompanyId);
