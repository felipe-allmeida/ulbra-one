using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the operational Contas a Pagar list = one installment with its payable context.</summary>
public sealed record PayableListItemResponse(
    PayableId PayableId, InstallmentId InstallmentId, int Sequence, int InstallmentCount,
    string CounterpartyName, string Description, CostCenterId CostCenterId, ExpenseCategoryId? CategoryId,
    DateOnly DueDate, decimal Amount, InstallmentStatus InstallmentStatus, PayableStatus PayableStatus,
    /// <summary>Date the daily sweep will process this installment (defaults to DueDate at creation).</summary>
    DateOnly ScheduledPaymentDate,
    /// <summary>True when this installment has been manually paused from the remittance sweep.</summary>
    bool IsHeld,
    /// <summary>The reason provided when holding this installment, or null if not held.</summary>
    string? HoldReason,
    /// <summary>Id of the remittance this installment belongs to, or null if not in a remittance.</summary>
    RemittanceId? RemittanceId,
    /// <summary>1-based sequence number of this installment within its remittance, or null if not in a remittance.</summary>
    int? RemittanceSequence);
