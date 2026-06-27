using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of a receivable stage tab = one RECEIVABLE (not one installment). Used by the receivable-grain
/// Reconciled tab. <c>FirstDueDate</c> is the earliest installment due date; <c>TotalAmount</c> is the title total.
/// </summary>
public sealed record ReceivableStageItemResponse(
    ReceivableId ReceivableId,
    string PayerName,
    string Description,
    CostCenterId CostCenterId,
    DateOnly FirstDueDate,
    int InstallmentCount,
    decimal TotalAmount,
    ReceivableStatus Status);
