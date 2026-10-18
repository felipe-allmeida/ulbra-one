using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>Response shape for a requisition attachment (metadata only; bytes are downloaded separately).</summary>
[SwaggerSchema("Metadata of a file attached to a requisition. The bytes are not here — they come from "
    + "the download endpoint.")]
public sealed record RequisitionAttachmentResponse(
    RequisitionAttachmentId Id,
    [property: SwaggerSchema("Original file name as uploaded.")]
    [property: SwaggerExample("nota-fiscal-12345.pdf")]
    string FileName,
    [property: SwaggerSchema("MIME type detected on upload, validated against the file's actual content "
        + "rather than trusted from the extension.")]
    [property: SwaggerExample("application/pdf")]
    string ContentType,
    [property: SwaggerSchema("File size in bytes.")]
    [property: SwaggerExample("184320")]
    long SizeBytes,
    [property: SwaggerSchema("What the file is. FiscalDocument is the one that clears the \"NF pendente\" "
        + "flag — a generic attachment does not, even when it happens to be the invoice.")]
    RequisitionAttachmentKind Kind,
    [property: SwaggerSchema("When the file was uploaded.")]
    DateTimeOffset CreatedAt)
{
    public static RequisitionAttachmentResponse From(RequisitionAttachment a) =>
        new(new RequisitionAttachmentId(a.Id), a.FileName, a.ContentType, a.SizeBytes, a.Kind, a.CreatedAt);
}

/// <summary>Response shape for an authored payment installment (AEL-844): due date, value to transfer,
/// the payment method and the boleto linha digitável (null unless paid by boleto). Enums serialize as
/// strings. The digitable line lets the form re-populate it when a returned-to-triage requisition is revisited.</summary>
[SwaggerSchema("One installment of the payment plan authored in the financial complement. Together they "
    + "replace the requester's original estimate with the amount actually programmed.")]
public sealed record RequisitionInstallmentResponse(
    [property: SwaggerSchema("Position in the plan, starting at 1.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("When this installment falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Amount of this installment, in BRL. The sum across installments is what "
        + "becomes the requisition's programmed total.")]
    [property: SwaggerExample("4166.67")]
    decimal Amount,
    [property: SwaggerSchema("How this installment is to be paid. It has to be a method the vendor "
        + "accepts, and it decides whether a boleto line is expected.")]
    PaymentMethod PaymentMethod,
    [property: SwaggerSchema("Boleto linha digitável, 47 or 48 digits. Null unless the method is Boleto.")]
    [property: SwaggerExample("34191790010104351004791020150008291070026000")]
    string? BoletoDigitableLine,
    [property: SwaggerSchema("Per-payment PIX key override (AEL-938). Null unless the method is Pix with an "
        + "override — it replaces the vendor's registered PIX key for this payment only.")]
    [property: SwaggerExample("pagamentos@fornecedor.com.br")]
    string? PixKeyOverride,
    [property: SwaggerSchema("Per-payment PIX \"copia e cola\" (EMV/BR Code, AEL-963). Null unless the method is "
        + "Pix paid by a QR — a static QR decodes to a key, a dynamic QR rides the CNAB forma 47 remittance.")]
    string? PixPayloadEmv)
{
    public static RequisitionInstallmentResponse From(RequisitionInstallment i) =>
        new(i.Sequence, i.DueDate, i.Amount, i.PaymentMethod, i.BoletoDigitableLine, i.PixKeyOverride, i.PixPayloadEmv);
}

/// <summary>Response shape for a launched monthly cycle of a recurring requisition (AEL-877): the competency,
/// amount, due date, method, boleto and the Finance Payable it generated (<see cref="PayableId"/>, null until
/// Finance confirms). <see cref="RealizedAmount"/> is the realizado booked to this cycle's competency (sum of
/// non-reversed payments), so the panel can show Pago/Parcial/Aberto (AEL-881). Enums serialize as strings.</summary>
[SwaggerSchema("One launched month of a recurring requisition (Conta Fixa). Each cycle generates its "
    + "own payable, so the recurrence is a series of independent titles rather than one instalment plan.")]
public sealed record RequisitionCycleResponse(
    [property: SwaggerSchema("Position in the recurrence, starting at 1.")]
    [property: SwaggerExample("3")]
    int Sequence,
    [property: SwaggerSchema("Competency this cycle covers (`YYYY-MM`) — the month whose budget it "
        + "consumes.")]
    [property: SwaggerExample("2026-10")]
    string Competency,
    [property: SwaggerSchema("Amount launched for this month, in BRL.")]
    [property: SwaggerExample("4200.00")]
    decimal Amount,
    [property: SwaggerSchema("Due date of this cycle's payable.")]
    [property: SwaggerExample("2026-10-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("How this cycle is to be paid.")]
    PaymentMethod PaymentMethod,
    [property: SwaggerSchema("Boleto linha digitável for this cycle. Null unless paid by boleto.")]
    string? BoletoDigitableLine,
    [property: SwaggerSchema("Per-payment PIX key override (AEL-938) for this cycle. Null unless paid by Pix "
        + "with an override.")]
    string? PixKeyOverride,
    [property: SwaggerSchema("Per-payment PIX \"copia e cola\" (EMV/BR Code, AEL-963) for this cycle. Null unless "
        + "paid by a PIX QR.")]
    string? PixPayloadEmv,
    [property: SwaggerSchema("The Finance payable this cycle generated. Null until Finance confirms it — "
        + "the two modules talk by event, so there is a window where the cycle exists and the payable does "
        + "not yet.")]
    Guid? PayableId,
    [property: SwaggerSchema("When the cycle was launched.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Realizado booked to this cycle's competency, in BRL — the sum of "
        + "non-reversed payments. Compare it against Amount to tell Pago from Parcial and from Aberto.")]
    [property: SwaggerExample("4200.00")]
    decimal RealizedAmount)
{
    public static RequisitionCycleResponse From(RequisitionCycle c, decimal realizedAmount) =>
        new(c.Sequence, c.Competency, c.Amount, c.DueDate, c.PaymentMethod, c.BoletoDigitableLine, c.PixKeyOverride, c.PixPayloadEmv, c.PayableId, c.CreatedAt, realizedAmount);
}

/// <summary>Response shape for a status transition (who/when/from→to). Enums serialize as strings.</summary>
[SwaggerSchema("One step of the requisition's history — who moved it, from where to where, and why. The "
    + "full list is the audit trail of the workflow.")]
public sealed record RequisitionTransitionResponse(
    [property: SwaggerSchema("What was done: submitted, triaged, approved, returned, cancelled.")]
    RequisitionAction Action,
    [property: SwaggerSchema("Status it was in before.")]
    RequisitionStatus FromStatus,
    [property: SwaggerSchema("Status it moved to.")]
    RequisitionStatus ToStatus,
    [property: SwaggerSchema("Who performed the action.")]
    UserId ActorId,
    [property: SwaggerSchema("Display name of the actor. Only the single-requisition endpoint resolves it; "
        + "the list endpoints leave it null rather than pay a directory lookup per row, so read it as "
        + "\"unresolved here\" and fall back to the id.")]
    [property: SwaggerExample("Maria Silva")]
    string? ActorName,
    [property: SwaggerSchema("The actor's Google profile photo, resolved under the same rule as ActorName. "
        + "Null for an unresolved actor and for a user with no photo — render initials instead.")]
    [property: SwaggerExample("https://lh3.googleusercontent.com/a/ACg8ocK")]
    string? ActorPictureUrl,
    [property: SwaggerSchema("When it happened.")]
    DateTimeOffset OccurredAt,
    [property: SwaggerSchema("Justification given. Required on the actions that send the requisition back "
        + "or cancel it, since that is what the requester reads to know what to fix.")]
    [property: SwaggerExample("Falta a cotação de um segundo fornecedor.")]
    string? Reason)
{
    public static RequisitionTransitionResponse From(
        RequisitionTransition t, IReadOnlyDictionary<Guid, UserProfile>? actors = null)
    {
        actors ??= EmptyActors;
        var profile = actors.GetValueOrDefault(t.ActorId);
        return new(t.Action, t.FromStatus, t.ToStatus, new UserId(t.ActorId),
            profile?.Name, profile?.PictureUrl, t.OccurredAt, t.Reason);
    }

    private static readonly Dictionary<Guid, UserProfile> EmptyActors = [];
}

/// <summary>One decision (Approve / ReturnForComplement / Cancel) taken by the current user, joined with
/// the requisition it belongs to — feeds the approver's "Histórico" tab (AEL-859).</summary>
[SwaggerSchema("A decision the current user took, joined with the requisition it was about. Feeds the "
    + "approver's own history — it is scoped to the caller, not a global decision log.")]
public sealed record RequisitionDecisionResponse(
    RequisitionId RequisitionId,
    [property: SwaggerSchema("Per-company sequential code of the requisition — what people cite.")]
    [property: SwaggerExample("482")]
    int Code,
    VendorId VendorId,
    [property: SwaggerSchema("Vendor name, denormalized so the row renders without a second call.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string VendorName,
    [property: SwaggerSchema("What was requested.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado do laboratório")]
    string Description,
    [property: SwaggerSchema("Value at the time of the decision, in BRL.")]
    [property: SwaggerExample("12500.00")]
    decimal EstimateValue,
    [property: SwaggerSchema("Where the requisition stands NOW, which may be past the decision — it "
        + "keeps moving after the approver acts.")]
    RequisitionStatus Status,
    [property: SwaggerSchema("The decision taken: approved, returned for complement, or cancelled.")]
    RequisitionAction DecisionAction,
    [property: SwaggerSchema("Justification given with the decision.")]
    string? DecisionReason,
    [property: SwaggerSchema("When the decision was taken.")]
    DateTimeOffset DecidedAt);

/// <summary>Response shape for a <see cref="Requisition"/>. Enums serialize as strings (global
/// JsonStringEnumConverter). <see cref="TotalCost"/> is derived (payment-plan total across installments).</summary>
[SwaggerSchema("A requisition — the request that starts the spend workflow. It carries three different "
    + "money figures on purpose: EstimateValue is what the requester asked for, TotalCost is what the "
    + "financial complement actually programmed, and RealizedValue is what has been paid.")]
public sealed record RequisitionResponse(
    RequisitionId Id,
    [property: SwaggerSchema("Per-company sequential code — what people cite instead of the id.")]
    [property: SwaggerExample("482")]
    int Code,
    [property: SwaggerSchema("Company the spend belongs to. Derived from the unit, never sent by the caller.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Unit the spend is charged to.")]
    UnitId UnitId,
    [property: SwaggerSchema("Cost center (natureza) whose monthly budget this consumes.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Project whose accumulated ceiling this also consumes. Optional — the cost "
        + "center's monthly budget is consumed either way.")]
    ProjectId? ProjectId,
    [property: SwaggerSchema("Vendor to be paid.")]
    VendorId VendorId,
    [property: SwaggerSchema("Requisition type. A recurring type (Conta Fixa) is what makes EndCompetency "
        + "and the cycles meaningful.")]
    RequisitionType Type,
    [property: SwaggerSchema("What is being requested — the headline shown throughout the workflow.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado do laboratório")]
    string Description,
    [property: SwaggerSchema("Extra context for triage and the approver.")]
    string? Observation,
    [property: SwaggerSchema("What the requester estimated, in BRL. Provisional: once the financial "
        + "complement is submitted, TotalCost is the figure that matters.")]
    [property: SwaggerExample("12500.00")]
    decimal EstimateValue,
    [property: SwaggerSchema("How to read EstimateValue: as the total for the whole window, or as the "
        + "amount per month.")]
    EstimateType EstimateType,
    [property: SwaggerSchema("First competency the spend lands in (`YYYY-MM`).")]
    [property: SwaggerExample("2026-08")]
    string StartCompetency,
    [property: SwaggerSchema("Last competency of the window (`YYYY-MM`), inclusive. Null on an open-ended "
        + "recurrence, which runs until it is explicitly terminated.")]
    [property: SwaggerExample("2026-12")]
    string? EndCompetency,
    [property: SwaggerSchema("How often the payment repeats over the window.")]
    PaymentFrequency? PaymentFrequency,
    [property: SwaggerSchema("How many installments the payment was expected to be split into. The "
        + "requester's intent — the authored plan in Installments is what is actually programmed.")]
    [property: SwaggerExample("12")]
    int? PaymentInstallmentsCount,
    [property: SwaggerSchema("Due date of the first installment — it anchors the payable's schedule.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly FirstDueDate,
    /// <summary>Issue (emission) date informed in the financial complement (AEL-844); null before submission.</summary>
    [property: SwaggerSchema("Emission date of the invoice, informed in the financial complement. Null "
        + "until that step is submitted.")]
    [property: SwaggerExample("2026-08-05")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("Programmed total, in BRL — the sum of the authored installments. Zero "
        + "before the financial complement is submitted, which is why it can read below EstimateValue on "
        + "a requisition still in triage.")]
    [property: SwaggerExample("50000.04")]
    decimal TotalCost,
    [property: SwaggerSchema("Realizado, in BRL — the sum of non-reversed payments across every "
        + "competency. This is money that actually left, not what was programmed.")]
    [property: SwaggerExample("8333.34")]
    decimal RealizedValue,
    [property: SwaggerSchema("Where the requisition stands in the workflow.")]
    RequisitionStatus Status,
    [property: SwaggerSchema("Files attached, oldest first.")]
    IReadOnlyList<RequisitionAttachmentResponse> Attachments,
    /// <summary>Installment plan authored in the financial complement (AEL-844); empty before submission.</summary>
    [property: SwaggerSchema("The authored payment plan. Empty before the financial complement is "
        + "submitted.")]
    IReadOnlyList<RequisitionInstallmentResponse> Installments,
    /// <summary>Monthly cycles launched after approval on a recurring requisition (AEL-877); empty otherwise.</summary>
    [property: SwaggerSchema("Monthly cycles launched after approval on a recurring requisition. Empty on "
        + "a non-recurring one, and empty before approval.")]
    IReadOnlyList<RequisitionCycleResponse> Cycles,
    /// <summary>The base Payable (from approval) once Finance confirms it (AEL-877); null before that / non-requisition.</summary>
    [property: SwaggerSchema("The payable generated on approval. Null until Finance confirms it — the "
        + "modules talk by event, so approval and the payable are not simultaneous.")]
    Guid? BasePayableId,
    /// <summary>Realizado booked to the base cycle's competency (<see cref="StartCompetency"/>) — lets the panel
    /// show the base row's Pago/Parcial/Aberto against <see cref="EstimateValue"/> (AEL-881).</summary>
    [property: SwaggerSchema("Realizado booked to the FIRST competency only, in BRL — not the whole "
        + "requisition. It exists so the base row of the recurrence panel can show its own status; "
        + "RealizedValue is the figure that covers everything.")]
    [property: SwaggerExample("4200.00")]
    decimal BaseRealizedAmount,
    [property: SwaggerSchema("The workflow history, oldest first.")]
    IReadOnlyList<RequisitionTransitionResponse> Transitions,
    [property: SwaggerSchema("When the requisition was created as a draft.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Who created it.")]
    [property: SwaggerExample("redacted-13@example.invalid")]
    string CreatedBy,
    /// <summary>When it entered the current status — the stage SLA base (also available in the lists).</summary>
    [property: SwaggerSchema("When it entered the current status. This is the clock the stage SLA is "
        + "measured from, so it moves on every transition.")]
    DateTimeOffset StatusChangedAt,
    /// <summary>The requisition already generated a Payable (post-approval status) but the requester hasn't
    /// provided a Nota Fiscal yet — no fiscal-document attachment (Kind=FiscalDocument or an .xml NF). Drives the
    /// "Nota Fiscal pendente" tag + the requester's "NF pendente" list. Approximation from the requisition's own
    /// attachments (Budgeting doesn't read the Finance Payable); an NF the accountant attaches directly on the
    /// Payable isn't reflected here.</summary>
    [property: SwaggerSchema("Whether the requester still owes a Nota Fiscal. An approximation computed "
        + "from the requisition's OWN attachments — Budgeting does not read the Finance payable, so an "
        + "invoice the accountant attaches directly there is not reflected here.")]
    bool FiscalDocumentPending,
    /// <summary>Vendor display name, denormalized for the list (batch-resolved cross-module). Empty on the
    /// single-entity endpoints, which resolve it client-side.</summary>
    [property: SwaggerSchema("Vendor name, denormalized for the lists. EMPTY on the single-requisition "
        + "endpoints, which expect the client to resolve it — do not render it blindly.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string VendorName = "")
{
    /// <summary><paramref name="actors"/> names (and pictures) the transition authors on the timeline; omit
    /// it where resolving one directory row per requisition would not pay for itself, and the timeline falls
    /// back to the actor id.</summary>
    public static RequisitionResponse From(Requisition r, IReadOnlyDictionary<Guid, UserProfile>? actors = null) =>
        new(new RequisitionId(r.Id), r.Code, new CompanyId(r.CompanyId), new UnitId(r.UnitId),
            new CostCenterId(r.CostCenterId), r.ProjectId is { } pid ? new ProjectId(pid) : null,
            new VendorId(r.VendorId),
            r.Type, r.Description, r.Observation, r.EstimateValue,
            r.EstimateType, r.StartCompetency, r.EndCompetency, r.PaymentFrequency, r.PaymentInstallmentsCount,
            r.FirstDueDate, r.IssueDate, r.TotalCost(), r.RealizedValue, r.Status,
            r.Attachments.OrderBy(a => a.CreatedAt).Select(RequisitionAttachmentResponse.From).ToList(),
            r.Installments.OrderBy(i => i.Sequence).Select(RequisitionInstallmentResponse.From).ToList(),
            r.Cycles.OrderBy(c => c.Sequence).Select(c => RequisitionCycleResponse.From(c, r.RealizedContributionFor(c.Competency))).ToList(),
            r.BasePayableId,
            r.RealizedContributionFor(r.StartCompetency),
            r.Transitions.OrderBy(t => t.OccurredAt).Select(t => RequisitionTransitionResponse.From(t, actors)).ToList(),
            r.CreatedAt, r.CreatedBy, r.StatusChangedAt, IsFiscalDocumentPending(r));

    /// <summary>List overload: carries the batch-resolved vendor name (avoids a per-row lookup).</summary>
    public static RequisitionResponse From(Requisition r, string vendorName) =>
        From(r) with { VendorName = vendorName };

    /// <summary>
    /// Post-approval (a Payable exists) and still without a Nota Fiscal — the requester is nagged to provide it.
    /// Type-aware (AEL-959), mirroring the payable-side gate: only a Nota Fiscal attachment
    /// (<see cref="RequisitionAttachmentKind.FiscalDocument"/>) or an .xml clears it; a boleto/fatura/generic
    /// document does NOT. (Supersedes the AEL-871 "any attachment silences the nag" heuristic, which predated
    /// per-file classification.)
    /// </summary>
    private static bool IsFiscalDocumentPending(Requisition r) =>
        r.Status is RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument
            or RequisitionStatus.Paid or RequisitionStatus.Finalized
        && !r.Attachments.Any(a =>
            a.Kind == RequisitionAttachmentKind.FiscalDocument
            || a.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
}

/// <summary>How many requisitions sit in one workflow stage, plus the summed estimated value there.
/// Powers the "Sua fila"/stage KPIs without shipping any rows.</summary>
[SwaggerSchema("Count and summed estimated value of the requisitions in a single workflow stage.")]
public sealed record RequisitionStageCountResponse(
    [property: SwaggerSchema("How many requisitions are in this stage.")]
    int Count,
    [property: SwaggerSchema("Sum of the estimated value of those requisitions, in BRL.")]
    decimal Total);

/// <summary>Per-status counts for the requisition stage KPIs (AEL). One grouped query replaces the former
/// per-status fan-out (one PageSize=1 list call per status). Keyed by the workflow status name; a status with
/// no requisitions is simply absent (the client defaults it to zero).</summary>
[SwaggerSchema("Requisition counts grouped by workflow status, for the stage KPIs. Statuses with no "
    + "requisitions are omitted.")]
public sealed record RequisitionStageCountsResponse(
    [property: SwaggerSchema("Count and summed value per workflow status, keyed by the status name "
        + "(e.g. Draft, Triage, Approval).")]
    IReadOnlyDictionary<string, RequisitionStageCountResponse> Stages);
