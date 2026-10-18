using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.PaymentsReport;

/// <summary>Query-string filters for the Relação de Pagamentos report. Bound via <c>[AsParameters]</c>.
/// Shared by the list, the summary ("Copiar Resumo") and the PDF export so all three see the same set.
/// Does NOT inherit pagination — the summary and export run over the whole filtered set.</summary>
public sealed record PaymentsReportRequest
{
    [SwaggerSchema("Company that owes the payables. Omit for the consolidated view across every company, "
        + "which only a consolidated role may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Units (Unidades) to keep. Multi-select: repeat the param for several. Empty = all units.")]
    public UnitId[]? UnitIds { get; set; }

    [SwaggerSchema("Naturezas orçamentárias (cost center NAMES) to keep. Multi-select: repeat the param for "
        + "several. Matched by name — not id — so the consolidated view keeps every company's same nature "
        + "(e.g. \"Ensino\"). Empty = all naturezas.")]
    public string[]? Natures { get; set; }

    [SwaggerSchema("Lifecycle stages to keep. Multi-select: repeat the param for several. Empty = every "
        + "status (the default report view).")]
    public PayableStatus[]? Statuses { get; set; }

    [SwaggerSchema("Keep installments due on or after this day (Vencimento range start).")]
    [SwaggerExample("2026-08-22")]
    public DateOnly? DueFrom { get; set; }

    [SwaggerSchema("Keep installments due on or before this day (Vencimento range end).")]
    [SwaggerExample("2026-09-18")]
    public DateOnly? DueTo { get; set; }

    [SwaggerSchema("Keep installments with a non-reversed payment on or after this day (Pagamento range "
        + "start — the realized window).")]
    public DateOnly? PayFrom { get; set; }

    [SwaggerSchema("Keep installments with a non-reversed payment on or before this day (Pagamento range end).")]
    public DateOnly? PayTo { get; set; }

    [SwaggerSchema("Include imported/external payables — those not born from a requisition. Off by default, "
        + "matching the legacy \"Mostrar Despesas Importadas\" toggle.")]
    public bool? ShowImported { get; set; }

    [SwaggerSchema("Free-text search over vendor name, document number, vendor CNPJ/CPF and the requisition "
        + "code (with or without the leading #).")]
    [SwaggerExample("#16916")]
    public string? Search { get; set; }
}

/// <summary>Flat, SQL-projected row of the report (one per installment). Typed IDs and display names are
/// resolved in <see cref="PaymentsReportQuery.EnrichAsync"/> after the set is materialized. Uses init
/// properties (not a positional ctor) so EF can map members back to columns for ORDER BY / GROUP BY.</summary>
public sealed record PaymentsReportRow
{
    public Guid PayableId { get; init; }
    public Guid InstallmentId { get; init; }
    public Guid CompanyId { get; init; }
    public int Sequence { get; init; }
    public int InstallmentCount { get; init; }
    public decimal Amount { get; init; }
    public DateOnly DueDate { get; init; }
    public InstallmentStatus InstallmentStatus { get; init; }
    public PayableStatus PayableStatus { get; init; }
    public Guid? OriginRequisitionId { get; init; }
    public int? OriginRequisitionCode { get; init; }
    public Guid? OriginUnitId { get; init; }
    public Guid CostCenterId { get; init; }
    public Guid VendorId { get; init; }
    public string CounterpartyName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? DocumentNumber { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public PayableOrigin Origin { get; init; }
}

/// <summary>Builds and enriches the Relação de Pagamentos report. The grain is the installment (parcela),
/// mirroring the legacy screen: one row per parcela whose due date (and optionally payment date) falls in the
/// filter window. Kept out of the endpoints so the list, summary and export share identical filtering.</summary>
public static class PaymentsReportQuery
{
    private sealed record ReqInfo(Guid Id, string CreatedBy, RequisitionType Type, string Description, string? Observation);
    private sealed record ApprovalInfo(Guid RequisitionId, Guid ActorId, DateTimeOffset OccurredAt);

    /// <summary>Applies the filters and fans payables out to installments, returning the projected rows in a
    /// stable order (due date, then payable, then sequence) so pagination never shuffles rows between pages.</summary>
    public static IQueryable<PaymentsReportRow> Build(IUnitOfWork uow, PaymentsReportRequest req)
    {
        var q = uow.ReadSet<Payable>();

        if (req.CompanyId is { } cid) q = q.Where(p => p.CompanyId == cid.Value);
        // Unidade/Natureza/Status são multi-seleção: filtro por conjunto (IN) quando há itens.
        if (req.UnitIds is { Length: > 0 } uids)
        {
            var unitGuids = uids.Select(u => u.Value).ToArray();
            q = q.Where(p => p.OriginUnitId != null && unitGuids.Contains(p.OriginUnitId.Value));
        }
        // Filter by nature NAME (not id): the same nature exists once per company, so a name match keeps every
        // company's "Ensino" in the consolidated view instead of only the one cost center the user picked.
        if (req.Natures is { Length: > 0 } natures)
        {
            var wanted = natures.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray();
            var costCenters = uow.ReadSet<CostCenter>();
            q = q.Where(p => costCenters.Any(cc => cc.Id == p.CostCenterId && wanted.Contains(cc.Name)));
        }
        if (req.Statuses is { Length: > 0 } statuses) q = q.Where(p => statuses.Contains(p.Status));
        // "Mostrar Despesas Importadas": imported/external = no originating requisition. Hidden by default.
        if (req.ShowImported != true) q = q.Where(p => p.OriginRequisitionId != null);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim();
            var lowered = term.ToLower();
            var docTerm = DocumentValidator.NormalizeCnpj(term);
            var codeSearch = int.TryParse(term.TrimStart('#'), out var code) ? code : (int?)null;
            var vendors = uow.ReadSet<VendorEntity>();
            q = q.Where(p =>
                p.CounterpartyName.ToLower().Contains(lowered)
                || (p.DocumentNumber != null && p.DocumentNumber.ToLower().Contains(lowered))
                || (docTerm != "" && vendors.Any(v => v.Id == p.VendorId && v.Document.Contains(docTerm)))
                || (codeSearch != null && p.OriginRequisitionCode == codeSearch));
        }

        var joined = q.SelectMany(p => p.Installments, (p, i) => new { p, i });

        if (req.DueFrom is { } df) joined = joined.Where(x => x.i.DueDate >= df);
        if (req.DueTo is { } dt) joined = joined.Where(x => x.i.DueDate <= dt);
        // Realized window: a single non-reversed payment must fall inside it (both bounds on the same payment).
        if (req.PayFrom is { } pf && req.PayTo is { } pt)
            joined = joined.Where(x => x.i.Payments.Any(pay => !pay.IsReversed && pay.PaymentDate >= pf && pay.PaymentDate <= pt));
        else if (req.PayFrom is { } pfrom)
            joined = joined.Where(x => x.i.Payments.Any(pay => !pay.IsReversed && pay.PaymentDate >= pfrom));
        else if (req.PayTo is { } pto)
            joined = joined.Where(x => x.i.Payments.Any(pay => !pay.IsReversed && pay.PaymentDate <= pto));

        // No ordering here — the list endpoint orders before paginating; summary/export aggregate. Object
        // initializer (not a positional ctor) so EF maps members to columns for the callers' ORDER BY / GROUP BY.
        return joined.Select(x => new PaymentsReportRow
        {
            PayableId = x.p.Id,
            InstallmentId = x.i.Id,
            CompanyId = x.p.CompanyId,
            Sequence = x.i.Sequence,
            InstallmentCount = x.p.Installments.Count,
            Amount = x.i.Amount,
            DueDate = x.i.DueDate,
            InstallmentStatus = x.i.Status,
            PayableStatus = x.p.Status,
            OriginRequisitionId = x.p.OriginRequisitionId,
            OriginRequisitionCode = x.p.OriginRequisitionCode,
            OriginUnitId = x.p.OriginUnitId,
            CostCenterId = x.p.CostCenterId,
            VendorId = x.p.VendorId,
            CounterpartyName = x.p.CounterpartyName,
            Description = x.p.Description,
            DocumentNumber = x.p.DocumentNumber,
            ApprovedBy = x.p.ApprovedBy,
            ApprovedAt = x.p.ApprovedAt,
            Origin = x.p.Origin,
        });
    }

    /// <summary>Resolves Solicitante/Aprovador/Unidade/Natureza display names and the vendor document for a
    /// materialized set of rows, in a handful of batch queries (no per-row DB hits). Mirrors the batch pattern
    /// in <c>ListPayables</c>. Safe for a page or the whole filtered set.</summary>
    public static async Task<List<PaymentReportItemResponse>> EnrichAsync(
        IUnitOfWork uow, IUserDirectory users, IReadOnlyList<PaymentsReportRow> rows, CancellationToken ct)
    {
        var reqIds = rows.Where(r => r.OriginRequisitionId != null)
            .Select(r => r.OriginRequisitionId!.Value).Distinct().ToList();

        var reqById = new Dictionary<Guid, ReqInfo>();
        var approvalByReq = new Dictionary<Guid, ApprovalInfo>();
        if (reqIds.Count > 0)
        {
            reqById = (await uow.ReadSet<Requisition>()
                    .Where(r => reqIds.Contains(r.Id))
                    .Select(r => new ReqInfo(r.Id, r.CreatedBy, r.Type, r.Description, r.Observation))
                    .ToListAsync(ct))
                .ToDictionary(r => r.Id);

            // Latest Approve transition per requisition (a requisition can re-enter approval, AEL-935).
            approvalByReq = (await uow.ReadSet<RequisitionTransition>()
                    .Where(t => reqIds.Contains(t.RequisitionId) && t.Action == RequisitionAction.Approve)
                    .Select(t => new ApprovalInfo(t.RequisitionId, t.ActorId, t.OccurredAt))
                    .ToListAsync(ct))
                .GroupBy(t => t.RequisitionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.OccurredAt).First());
        }

        var userIds = new HashSet<Guid>();
        foreach (var r in reqById.Values)
            if (Guid.TryParse(r.CreatedBy, out var g)) userIds.Add(g);
        foreach (var a in approvalByReq.Values) userIds.Add(a.ActorId);
        foreach (var row in rows.Where(r => r.OriginRequisitionId == null))
            if (Guid.TryParse(row.ApprovedBy, out var g)) userIds.Add(g);
        var names = (await users.GetProfilesAsync(userIds.ToList(), ct)).ToDictionary(kv => kv.Key, kv => kv.Value.Name);

        var unitIds = rows.Where(r => r.OriginUnitId != null).Select(r => r.OriginUnitId!.Value).Distinct().ToList();
        var unitNames = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<Unit>().Where(u => unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);

        var ccIds = rows.Select(r => r.CostCenterId).Distinct().ToList();
        var ccNames = (await uow.ReadSet<CostCenter>().Where(c => ccIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        var vendorIds = rows.Select(r => r.VendorId).Distinct().ToList();
        var vendorDocs = (await uow.ReadSet<VendorEntity>().Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Document }).ToListAsync(ct)).ToDictionary(v => v.Id, v => v.Document);

        var result = new List<PaymentReportItemResponse>(rows.Count);
        foreach (var row in rows)
        {
            ReqInfo? req = row.OriginRequisitionId is { } rid && reqById.TryGetValue(rid, out var r) ? r : null;

            string? requesterName = null;
            if (req is not null && Guid.TryParse(req.CreatedBy, out var reqUser) && names.TryGetValue(reqUser, out var rn))
                requesterName = rn;

            // Aprovador/Data: the requisition's Approve transition wins (the Finance ApprovedBy is a
            // "system:req:…" sentinel for requisition payables); manual payables use Payable.ApprovedBy; when the
            // title is approved but the actor is unresolvable (system/import), show "Não identificado".
            string? approverName = null;
            DateTimeOffset? approvedAt = null;
            if (row.OriginRequisitionId is { } areq && approvalByReq.TryGetValue(areq, out var appr))
            {
                approvedAt = appr.OccurredAt;
                approverName = names.TryGetValue(appr.ActorId, out var an) ? an : "Não identificado";
            }
            else if (Guid.TryParse(row.ApprovedBy, out var pab) && names.TryGetValue(pab, out var pn))
            {
                approverName = pn;
                approvedAt = row.ApprovedAt;
            }
            else if (row.ApprovedAt is { } aa)
            {
                approverName = "Não identificado";
                approvedAt = aa;
            }

            var unitName = row.OriginUnitId is { } uid2 && unitNames.TryGetValue(uid2, out var un) ? un : null;
            var natureName = ccNames.TryGetValue(row.CostCenterId, out var cn) ? cn : null;
            var vendorDoc = vendorDocs.TryGetValue(row.VendorId, out var vd) && vd != "" ? vd : null;
            var isFixedExpense = req?.Type == RequisitionType.FixedExpense;

            result.Add(new PaymentReportItemResponse(
                new PayableId(row.PayableId), new InstallmentId(row.InstallmentId),
                new CompanyId(row.CompanyId),
                row.OriginRequisitionCode, requesterName,
                row.OriginUnitId is { } uid3 ? new UnitId(uid3) : null, unitName,
                new CostCenterId(row.CostCenterId), natureName,
                approverName, approvedAt,
                row.CounterpartyName, vendorDoc,
                row.Amount, row.DueDate, row.Sequence, row.InstallmentCount,
                row.PayableStatus, row.InstallmentStatus,
                StatusLabel(row.PayableStatus, row.InstallmentStatus),
                PaymentTypeLabel(row.Sequence, row.InstallmentCount, isFixedExpense),
                req?.Description ?? row.Description, req?.Observation));
        }

        return result;
    }

    /// <summary>Legacy pt-BR status wording used on the report and its PDF.</summary>
    public static string StatusLabel(PayableStatus payableStatus, InstallmentStatus installmentStatus) => payableStatus switch
    {
        PayableStatus.Paid => "Pago",
        PayableStatus.Scheduled => installmentStatus switch
        {
            InstallmentStatus.InRemittance => "Fila Remessa",
            InstallmentStatus.Paid => "Pago",
            _ => "Fila Pagamento",
        },
        PayableStatus.PendingApproval => "Pendente Aprovação",
        PayableStatus.PendingClassification => "Em Triagem",
        PayableStatus.PendingTaxAnalysis => "Avaliação Imposto",
        PayableStatus.PendingAccounting => "Em Contabilização",
        PayableStatus.Received => "Recebido",
        PayableStatus.Rejected => "Devolvido",
        PayableStatus.Cancelled => "Cancelada",
        _ => payableStatus.ToString(),
    };

    /// <summary>Payment-frequency label per row: "Conta fixa" for a recurring requisition, otherwise
    /// "Pgto. Único" for a single parcela or "Pgto {seq}/{count}".</summary>
    public static string PaymentTypeLabel(int sequence, int installmentCount, bool isFixedExpense) =>
        isFixedExpense ? "Conta fixa"
        : installmentCount <= 1 ? "Pgto. Único"
        : $"Pgto {sequence}/{installmentCount}";
}
