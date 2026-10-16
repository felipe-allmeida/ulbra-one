using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the operational Contas a Receber list = one installment with its receivable context.
/// Projected without the receipts tree (scale ~19k titles). <c>IsOverdue</c> is derived from the due date.</summary>
[SwaggerSchema("One row of the Contas a Receber list. The grain is the INSTALLMENT. The receipts tree "
    + "is deliberately left out — at this scale the list would not survive carrying it.")]
public sealed record ReceivableListItemResponse(
    ReceivableId ReceivableId,
    ReceivableInstallmentId InstallmentId,
    [property: SwaggerSchema("Position of this installment within the receivable, starting at 1.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("How many installments the receivable has.")]
    [property: SwaggerExample("3")]
    int InstallmentCount,
    [property: SwaggerSchema("Customer name as recorded on the receivable.")]
    [property: SwaggerExample("Prefeitura Municipal de Canoas")]
    string PayerName,
    [property: SwaggerSchema("What the receivable is for.")]
    [property: SwaggerExample("Convênio de estágio — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the revenue is attributed to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Revenue category, when one was assigned.")]
    Guid? RevenueCategoryId,
    [property: SwaggerSchema("When this installment falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Amount owed on this installment, in BRL.")]
    [property: SwaggerExample("6000.00")]
    decimal Amount,
    [property: SwaggerSchema("Received so far, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal ReceivedAmount,
    [property: SwaggerSchema("Where this installment stands.")]
    ReceivableInstallmentStatus InstallmentStatus,
    [property: SwaggerSchema("Where the whole receivable stands.")]
    ReceivableStatus ReceivableStatus,
    [property: SwaggerSchema("Whether the installment is past due and still open. Derived from the due "
        + "date at read time, so it is always relative to today.")]
    bool IsOverdue);
