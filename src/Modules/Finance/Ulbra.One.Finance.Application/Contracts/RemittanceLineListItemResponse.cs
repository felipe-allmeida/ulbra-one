using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of the flat "Títulos gerados" view = a remittance line joined to its remittance and payable, so
/// the títulos across every remittance can be listed and filtered by unidade/natureza/forma/status. Shows
/// which remittance a título is in and the bank's outcome (paid/returned).
/// </summary>
[SwaggerSchema("A remittance line joined to its remittance and payable, for the flat Títulos view.")]
public sealed record RemittanceLineListItemResponse(
    [property: SwaggerSchema("Identifier of the remittance line.")]
    Guid Id,
    [property: SwaggerSchema("Remittance the line belongs to.")]
    RemittanceId RemittanceId,
    [property: SwaggerSchema("Sequential number (NSA) of the remittance file.")]
    [property: SwaggerExample("42")]
    int RemittanceSequence,
    [property: SwaggerSchema("Payment-form category of the remittance (Pix/Ted/BoletoBancario/Convenio). "
        + "Null on pre-Onda-6 mixed files.")]
    [property: SwaggerExample("Pix")]
    string? Category,
    [property: SwaggerSchema("When the remittance was generated.")]
    DateTimeOffset GeneratedAt,
    [property: SwaggerSchema("Payable behind the título.")]
    PayableId PayableId,
    [property: SwaggerSchema("Installment being remitted.")]
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Friendly requisition code of the payable (the \"#\"). Null when not "
        + "requisition-sourced.")]
    [property: SwaggerExample("16916")]
    int? RequisitionCode,
    [property: SwaggerSchema("Vendor name as written into the file.")]
    [property: SwaggerExample("CIGAME COMERCIO DE MATERIAL ELETRICO LTDA")]
    string FavoredName,
    [property: SwaggerSchema("Unit (Unidade) of the payable. Null when it has none.")]
    UnitId? UnitId,
    [property: SwaggerSchema("Unit display name. Null when there is no unit.")]
    string? UnitName,
    [property: SwaggerSchema("Cost center (Natureza) of the payable.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Cost center display name (Natureza).")]
    string? NatureName,
    [property: SwaggerSchema("Payment method declared for the line.")]
    [property: SwaggerExample("Pix")]
    string Method,
    [property: SwaggerSchema("Amount to transfer, in BRL.")]
    [property: SwaggerExample("4732.82")]
    decimal Amount,
    [property: SwaggerSchema("Título due date (vencimento). Null for PIX.")]
    DateOnly? DueDate,
    [property: SwaggerSchema("What the bank said about the line: Pending, Paid or Returned.")]
    [property: SwaggerExample("Paid")]
    string Status,
    [property: SwaggerSchema("Date the bank settled the line, from the return file. Null while pending.")]
    DateOnly? EffectiveDate,
    [property: SwaggerSchema("Rejection reason from the return file, when the line was returned.")]
    string? ReturnReason);
