using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of the "Relação de Pagamentos" report = one INSTALLMENT (parcela), mirroring the legacy screen:
/// the amount and due date are the installment's, and <c>PaymentTypeLabel</c> tells which parcela it is.
/// The Solicitante/Aprovador/Unidade/Natureza columns are resolved from the originating requisition and the
/// user directory, so they are null for payables not born from a requisition.
/// </summary>
[SwaggerSchema("One row of the Relação de Pagamentos report. The grain is the INSTALLMENT (parcela): the "
    + "amount and due date are the parcela's, not the title total.")]
public sealed record PaymentReportItemResponse(
    PayableId PayableId,
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Owning company, surfaced so the consolidated \"Todas as empresas\" report can "
        + "show which one each row belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Friendly code of the requisition that generated the payable (the \"#\" column). "
        + "Null when the payable was not born from a requisition.")]
    [property: SwaggerExample("16916")]
    int? RequisitionCode,
    [property: SwaggerSchema("Name of the requester (Solicitante) — the creator of the originating requisition. "
        + "Null for imported/external payables or when the user cannot be resolved.")]
    [property: SwaggerExample("Nayara da Rocha Silveira")]
    string? RequesterName,
    [property: SwaggerSchema("Unit (Unidade) tagged on the payable. Null unless requisition-sourced or "
        + "manually tagged.")]
    UnitId? UnitId,
    [property: SwaggerSchema("Unit display name. Null when there is no unit.")]
    [property: SwaggerExample("Ulbra S.A")]
    string? UnitName,
    [property: SwaggerSchema("Cost center (Natureza orçamentária) the expense is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Cost center display name (the \"Natureza\" column).")]
    [property: SwaggerExample("ENSINO")]
    string? NatureName,
    [property: SwaggerSchema("Name of the approver (Aprovador). \"Não identificado\" when the title was "
        + "approved but the actor cannot be resolved (system/import); null when it is not yet approved.")]
    [property: SwaggerExample("Cristina Caberlon")]
    string? ApproverName,
    [property: SwaggerSchema("When the title was approved. Null when it is not yet approved.")]
    DateTimeOffset? ApprovedAt,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("CIGAME COMERCIO DE MATERIAL ELETRICO LTDA")]
    string CounterpartyName,
    [property: SwaggerSchema("Vendor CNPJ/CPF, as stored in the Registry. Null when the vendor has none.")]
    [property: SwaggerExample("09.721.504/0001-75")]
    string? VendorDocument,
    [property: SwaggerSchema("Installment amount in BRL (gross — the value the parcela is due for).")]
    [property: SwaggerExample("4732.82")]
    decimal Amount,
    [property: SwaggerSchema("Installment due date (Vencimento).")]
    [property: SwaggerExample("2026-08-22")]
    DateOnly DueDate,
    [property: SwaggerSchema("Which installment this row is, 1-based.")]
    [property: SwaggerExample("2")]
    int InstallmentSequence,
    [property: SwaggerSchema("How many installments the payable has in total.")]
    [property: SwaggerExample("2")]
    int InstallmentCount,
    [property: SwaggerSchema("Lifecycle stage of the payable.")]
    PayableStatus PayableStatus,
    [property: SwaggerSchema("Settlement state of this installment.")]
    InstallmentStatus InstallmentStatus,
    [property: SwaggerSchema("Human-readable status label, in pt-BR, matching the legacy report wording "
        + "(e.g. \"Fila Pagamento\", \"Em Triagem\", \"Pago\").")]
    [property: SwaggerExample("Fila Pagamento")]
    string StatusLabel,
    [property: SwaggerSchema("Payment-frequency label for this row, e.g. \"Pgto. Único\", \"Pgto 2/6\" or "
        + "\"Conta fixa\".")]
    [property: SwaggerExample("Pgto 2/2")]
    string PaymentTypeLabel,
    [property: SwaggerSchema("What the payable is for (Descrição). May be long; shown in the optional PDF "
        + "detail block.")]
    string? Description,
    [property: SwaggerSchema("Free-text observation (Observação) from the originating requisition, when any.")]
    string? Observation);
