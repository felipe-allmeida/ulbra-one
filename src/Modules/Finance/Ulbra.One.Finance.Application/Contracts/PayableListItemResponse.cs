using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the operational Contas a Pagar list = one installment with its payable context.</summary>
public sealed record PayableListItemResponse(
    PayableId PayableId, InstallmentId InstallmentId, int Sequence, int InstallmentCount,
    string CounterpartyName, string Description, CostCenterId CostCenterId, ExpenseCategoryId? CategoryId,
    DateOnly DueDate, decimal Amount, InstallmentStatus InstallmentStatus, PayableStatus PayableStatus);
