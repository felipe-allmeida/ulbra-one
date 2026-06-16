using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the operational Contas a Receber list = one installment with its receivable context.
/// Projected without the receipts tree (scale ~19k titles). <c>IsOverdue</c> is derived from the due date.</summary>
public sealed record ReceivableListItemResponse(
    ReceivableId ReceivableId, ReceivableInstallmentId InstallmentId, int Sequence, int InstallmentCount,
    string PayerName, string Description, CostCenterId CostCenterId, Guid? RevenueCategoryId,
    DateOnly DueDate, decimal Amount, decimal ReceivedAmount, ReceivableInstallmentStatus InstallmentStatus,
    ReceivableStatus ReceivableStatus, bool IsOverdue);
