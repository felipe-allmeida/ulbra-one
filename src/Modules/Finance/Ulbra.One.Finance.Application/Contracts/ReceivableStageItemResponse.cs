using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of a receivable stage tab = one RECEIVABLE (not one installment). Used by the receivable-grain
/// Reconciled tab. <c>FirstDueDate</c> is the earliest installment due date; <c>TotalAmount</c> is the title total.
/// </summary>
[SwaggerSchema("One row of a receivable stage tab. The grain is the TITLE, not the installment.")]
public sealed record ReceivableStageItemResponse(
    ReceivableId ReceivableId,
    [property: SwaggerSchema("Customer name as recorded on the receivable.")]
    [property: SwaggerExample("Prefeitura Municipal de Canoas")]
    string PayerName,
    [property: SwaggerSchema("What the receivable is for.")]
    [property: SwaggerExample("Convênio de estágio — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the revenue is attributed to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Earliest due date across the installments — what the list sorts and ages by.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly FirstDueDate,
    [property: SwaggerSchema("How many installments the receivable has.")]
    [property: SwaggerExample("3")]
    int InstallmentCount,
    [property: SwaggerSchema("Total owed, in BRL.")]
    [property: SwaggerExample("18000.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Where the receivable stands.")]
    ReceivableStatus Status);
