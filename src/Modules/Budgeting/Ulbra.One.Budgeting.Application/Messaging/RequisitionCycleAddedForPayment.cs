using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) when a monthly cycle is launched on a recurring requisition already in
/// payment (AEL-877) — the "fatura do mês". The Finance consumer materializes ONE additional Payable for the
/// cycle (tagged with <see cref="Competency"/>), which then rides the normal Finance pipeline. No re-approval:
/// the initial approval covers the future invoices (mirrors the legacy app). Cross-module refs are loose Guids;
/// the payment plan is a single <see cref="PlannedInstallment"/> (the cycle's due date / amount / method / boleto),
/// reusing the same shape as <see cref="RequisitionApprovedForPayment"/>.</summary>
public sealed record RequisitionCycleAddedForPayment(
    Guid RequisitionId,
    Guid CompanyId,
    Guid UnitId,
    Guid CostCenterId,
    Guid VendorId,
    string Description,
    // The cycle's competency ("YYYY-MM") — the Finance dedup key (one Payable per requisition per cycle) and the
    // default for the Payable's IssueDate/data de emissão (first day of the competency).
    string Competency,
    // Service/Product nature, carried as a neutral string like RequisitionApprovedForPayment.
    string PayableType,
    // The single installment of this cycle (due date, amount, method and boleto linha digitável).
    PlannedInstallment Installment,
    // The requisition's friendly code, denormalized onto the Payable for display/search.
    int? Code = null,
    // Conta Fixa (AEL-881): the cycle's Payable requires its Nota Fiscal before it can be scheduled — the Finance
    // consumer stamps the Payable so it parks in PendingAccounting until the NF is uploaded. Defaulted false.
    bool RequiresFiscalDocument = false,
    // Prestação de Serviço / Conta Fixa: the cycle's Payable must pass the Tributário's análise tributária before
    // scheduling. The Finance consumer stamps it so Approve() routes to PendingTaxAnalysis. Defaulted false.
    bool RequiresTaxAnalysis = false,
    // NF anexada no próprio lançamento da fatura (AEL-877): viaja com a criação do ciclo e o consumer a copia
    // para o Payable do ciclo no momento em que ele é materializado (sem corrida; nunca vira anexo geral da
    // requisição). Nula quando a fatura é lançada sem NF (aí o Payable fica em "NF pendente" — R3).
    RequisitionAttachmentRef? Attachment = null)
{
    /// <summary>Builds the event from a launched cycle. The boleto (when paid by boleto) travels inside the
    /// <see cref="PlannedInstallment"/>, exactly as in the approval path. The optional NF <paramref name="attachment"/>
    /// is stored by the endpoint and rides along so the Finance consumer can copy it onto the cycle's Payable.</summary>
    public static RequisitionCycleAddedForPayment From(Requisition r, RequisitionCycle c, RequisitionAttachmentRef? attachment = null) =>
        new(r.Id, r.CompanyId, r.UnitId, r.CostCenterId, r.VendorId, r.Description, c.Competency,
            RequisitionApprovedForPayment.PayableTypeFrom(r.Type),
            new PlannedInstallment(1, c.DueDate, c.Amount, c.PaymentMethod.ToString(), c.BoletoDigitableLine, c.PixKeyOverride, c.PixPayloadEmv),
            r.Code, r.IsFixedExpense, r.RequiresTaxAnalysis, attachment);
}
