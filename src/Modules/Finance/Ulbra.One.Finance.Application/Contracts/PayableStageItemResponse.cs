using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of a stage tab = one PAYABLE (not one installment). Used by the payable-grain stages
/// (Entry / Classification / Approval / Reconciled), where you act on the title, not the parcela.
/// <c>FirstDueDate</c> is the earliest installment due date; <c>TotalAmount</c> is the title total.
/// </summary>
public sealed record PayableStageItemResponse(
    PayableId PayableId,
    string CounterpartyName,
    string Description,
    CostCenterId CostCenterId,
    ExpenseCategoryId? CategoryId,
    DateOnly FirstDueDate,
    int InstallmentCount,
    decimal TotalAmount,
    /// <summary>Sum of the payable's withholdings (0 when there are none).</summary>
    decimal TotalWithheld,
    /// <summary>TotalAmount − TotalWithheld — final once accounting completes (withholdings lock at Scheduled).</summary>
    decimal NetTotal,
    PayableStatus Status,
    PayableOrigin Origin,
    bool HasFiscalDocument);
