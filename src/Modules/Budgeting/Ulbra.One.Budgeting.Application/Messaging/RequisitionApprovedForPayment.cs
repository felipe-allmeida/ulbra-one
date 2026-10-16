using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>One installment of the payment plan carried inside <see cref="RequisitionApprovedForPayment"/>.
/// Budgeting owns the payment cadence, so it computes the concrete due dates/amounts and Finance just
/// materializes them — no PaymentFrequency enum crosses the module boundary. <paramref name="PaymentMethod"/>
/// (the payment method authored per installment, AEL-844) is carried as a neutral string ("Pix"/"Ted"/…, like
/// PayableType/Kind) with a default so messages published before this field existed deserialize as "Other".
/// <paramref name="BoletoDigitableLine"/> is the boleto digitable line authored per installment (null unless
/// paid by boleto); Finance stamps it on the installment so the CNAB240 remittance can emit it (Segment J/O).
/// <paramref name="PixKeyOverride"/> (AEL-938) is the per-payment PIX key authored when paid by PIX (null
/// otherwise); Finance stamps it so the CNAB240 Segment B uses it instead of the vendor's registered key.
/// Defaulted so messages published before this field deserialize without it.</summary>
public sealed record PlannedInstallment(int Sequence, DateOnly DueDate, decimal Amount, string PaymentMethod = "Other", string? BoletoDigitableLine = null, string? PixKeyOverride = null);

/// <summary>An attachment carried from the requisition so the Finance side can reproduce it on the Payable.
/// Only metadata + the object-storage key travel; the Finance consumer copies the blob into its own key so
/// the Payable owns an independent copy (the requisition's blob lifecycle can't break the Payable's).
/// <paramref name="Kind"/> ("Other"/"FiscalDocument") is a neutral string (like PayableType above) with a
/// default so messages published before this field existed deserialize as "Other" — a marked fiscal document is
/// promoted to the Payable's fiscal document by the Finance consumer.</summary>
public sealed record RequisitionAttachmentRef(
    string FileName, string ContentType, long SizeBytes, string StoragePath, Guid UploadedById, string Kind = "Other");

/// <summary>Published (EF bus outbox) when a requisition enters <c>InPayment</c> — the "approved for payment"
/// moment reached via the Approver approval. The Finance consumer creates exactly one Payable from it,
/// idempotently.
/// Cross-module refs are loose Guids. <paramref name="Competency"/> ("YYYY-MM") is the requisition's start
/// competency — the default for the Payable's IssueDate (later overridden by an attached fiscal
/// document's own emission date). <paramref name="UnitId"/> is stamped on the Payable for Finance-side reporting
/// only (Finance has no Unit dimension).</summary>
public sealed record RequisitionApprovedForPayment(
    Guid RequisitionId,
    Guid CompanyId,
    Guid UnitId,
    Guid CostCenterId,
    Guid VendorId,
    string Description,
    string Competency,
    // Service/Product nature the Payable needs to be approvable and to compute source withholdings.
    // Carried as a neutral string ("Service"/"Product") so Finance's PayableType enum doesn't cross the
    // boundary — mirrors how PayableScheduled carries TaxType as a string.
    string PayableType,
    // Emission date authored in the financial complement (AEL-844); overrides the
    // competency-derived default on the Payable's IssueDate. Null for messages published before this field.
    DateOnly? IssueDate,
    IReadOnlyList<PlannedInstallment> Installments,
    IReadOnlyList<RequisitionAttachmentRef> Attachments,
    // The requisition's friendly sequential code, denormalized onto the Payable for display/search on the
    // Accounting review list. Defaulted so messages published before this field deserialize without it.
    int? Code = null,
    // Conta Fixa (AEL-881) requires the fiscal document per invoice: the Finance consumer stamps the Payable so it
    // does NOT auto-schedule without a fiscal document (parks in PendingAccounting until one is uploaded). Defaulted false so
    // older messages — and every non-Conta-Fixa requisition — keep the current auto-schedule behaviour.
    bool RequiresFiscalDocument = false,
    // Prestação de Serviço / Conta Fixa: the Payable must pass the Tributário's análise tributária before scheduling.
    // The Finance consumer stamps the Payable so Approve() routes it to PendingTaxAnalysis. Defaulted false so older
    // messages — and Compra/Projeto requisitions — keep the current flow.
    bool RequiresTaxAnalysis = false)
{
    /// <summary>Builds the event from a requisition that has just entered <c>InPayment</c>. The payment plan
    /// (due dates + amounts + payment method) comes from the authored installments when present (AEL-844),
    /// otherwise the domain computes it — Finance materializes it verbatim. Attachments are carried so the
    /// Payable ends up with the same files the requester attached (quote/contract/fiscal document). Requires the caller to
    /// have loaded <c>r.Attachments</c> and <c>r.Installments</c> (the approval endpoints Include them).</summary>
    public static RequisitionApprovedForPayment From(Requisition r) =>
        new(r.Id, r.CompanyId, r.UnitId, r.CostCenterId, r.VendorId, r.Description, r.StartCompetency,
            PayableTypeFrom(r.Type), r.IssueDate,
            InstallmentsFrom(r),
            r.Attachments.Select(a => new RequisitionAttachmentRef(a.FileName, a.ContentType, a.SizeBytes, a.StoragePath, a.UploadedById, a.Kind.ToString())).ToList(),
            r.Code, r.IsFixedExpense, r.RequiresTaxAnalysis);

    /// <summary>Authored installments (with the per-installment payment method) when present; otherwise the
    /// domain-computed schedule with a defaulted method.</summary>
    private static IReadOnlyList<PlannedInstallment> InstallmentsFrom(Requisition r) =>
        r.Installments.Count > 0
            ? r.Installments.OrderBy(i => i.Sequence)
                .Select(i => new PlannedInstallment(i.Sequence, i.DueDate, i.Amount, i.PaymentMethod.ToString(), i.BoletoDigitableLine, i.PixKeyOverride)).ToList()
            : r.BuildPaymentSchedule().Select(p => new PlannedInstallment(p.Sequence, p.DueDate, p.Amount)).ToList();

    /// <summary>Maps the requisition type to the Payable's service/product nature. Purchase = goods = Product
    /// (no source withholding); every other type (Service Provision, Conta Fixa, Project)
    /// is treated as Service so withholdings are computed. Best-effort: if the fiscal document uploaded later
    /// says otherwise (NF-e = Product), Finance overrides the type and recomputes withholdings.</summary>
    internal static string PayableTypeFrom(RequisitionType type) =>
        type == RequisitionType.Purchase ? "Product" : "Service";
}
