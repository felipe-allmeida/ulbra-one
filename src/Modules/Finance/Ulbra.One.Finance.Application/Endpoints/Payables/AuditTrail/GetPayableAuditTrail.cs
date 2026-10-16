using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Auditing;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AuditTrail;

public sealed class GetPayableAuditTrail : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("{id}/audit-trail", Handle).WithName("GetPayableAuditTrail").WithTags("Finance")
            .WithSummary("Gets a payable\u0027s audit trail.")
            .WithDescription("Everything that happened to one payable, in order, with who did it - read from the audit "
                + "store and resolved to user names. Read-only and append-only: nothing here can be edited.")
            .RequirePermission(Permissions.PayablesRead).Produces<IReadOnlyList<AuditTrailEventResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        IUnitOfWork uow,
        IAuditReader auditReader,
        IUserDirectory users,
        CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>()
            .Include(x => x.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (payable is null)
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // --- AuditLog-sourced timestamps (hybrid "C"): actions lacking a dedicated entity timestamp. ---
        DateTimeOffset? cancelledAt = null;
        if (payable.Status == PayableStatus.Cancelled)
        {
            cancelledAt = await auditReader.GetLatestChangeAsync("Payable", payable.Id.ToString(), "CancelReason", ct);
        }

        var reversedPaymentIds = payable.Installments
            .SelectMany(i => i.Payments).Where(p => p.IsReversed).Select(p => p.Id.ToString()).ToList();
        var paymentReversalTimes = new Dictionary<Guid, DateTimeOffset>();
        if (reversedPaymentIds.Count > 0)
        {
            var reversalsByKey = await auditReader.GetLatestChangesAsync("Payment", reversedPaymentIds, "IsReversed", ct);
            foreach (var (key, timestamp) in reversalsByKey)
                if (Guid.TryParse(key, out var pid))
                    paymentReversalTimes[pid] = timestamp;
        }

        // Durable return/reopen history (survives Reopen clearing RejectedAt) — AEL-935.
        var history = await auditReader.GetUpdateHistoryAsync("Payable", payable.Id.ToString(), ct);

        // --- Resolve actor names for every Guid actor referenced. ---
        var guidActors = new List<Guid>();
        foreach (var change in history)
            if (Guid.TryParse(change.ActorUserId, out var hg)) guidActors.Add(hg);
        foreach (var i in payable.Installments)
            foreach (var p in i.Payments)
                guidActors.Add(p.CreatedById);
        foreach (var a in payable.Attachments)
            guidActors.Add(a.UploadedById);
        if (Guid.TryParse(payable.CreatedBy, out var cg)) guidActors.Add(cg);
        if (Guid.TryParse(payable.ApprovedBy, out var ag)) guidActors.Add(ag);
        if (Guid.TryParse(payable.RejectedBy, out var rg)) guidActors.Add(rg);
        if (Guid.TryParse(payable.UpdatedBy, out var ug)) guidActors.Add(ug);

        // The context pass appends the actors only it knows about (the operator who generated each
        // remittance), so the profiles are resolved once, after it — resolving before would leave those
        // events labelled with a raw Guid.
        var context = await BuildContextAsync(payable, uow, guidActors, ct);
        var profiles = await users.GetProfilesAsync(guidActors, ct);
        var events = PayableAuditTrailBuilder.Build(payable, profiles, cancelledAt, paymentReversalTimes, history, context);
        return TypedResults.Ok(events);
    }

    /// <summary>
    /// Gathers what the aggregate does not carry: the originating requisition (Budgeting), the ledger account it
    /// was classified to (Accounting) and the remittances it travelled in (Finance). Each piece is optional — a
    /// missing one just omits its event rather than failing the timeline.
    /// </summary>
    private static async Task<PayableTrailContext> BuildContextAsync(
        Payable payable, IUnitOfWork uow, List<Guid> guidActors, CancellationToken ct)
    {
        DateTimeOffset? reqCreatedAt = null;
        string? reqCreatedBy = null;
        if (payable.OriginRequisitionId is { } requisitionId)
        {
            var req = await uow.ReadSet<Requisition>()
                .Where(r => r.Id == requisitionId)
                .Select(r => new { r.CreatedAt, r.CreatedBy })
                .FirstOrDefaultAsync(ct);
            if (req is not null) { reqCreatedAt = req.CreatedAt; reqCreatedBy = req.CreatedBy; }
        }

        // Only meaningful for the cost-centre auto-approval ("system"); a requisition title was approved upstream.
        decimal? autoLimit = null;
        if (payable.ApprovedBy == "system")
        {
            autoLimit = await uow.ReadSet<CostCenter>()
                .Where(c => c.Id == payable.CostCenterId)
                .Select(c => (decimal?)c.AutoApprovalLimit)
                .FirstOrDefaultAsync(ct);
        }

        string? accountCode = null, accountName = null;
        if (payable.AccountingAccountId is { } accountId)
        {
            var acct = await uow.ReadSet<LedgerAccount>()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.Code, a.Name })
                .FirstOrDefaultAsync(ct);
            if (acct is not null) { accountCode = acct.Code; accountName = acct.Name; }
        }

        var remittances = await (
            from line in uow.ReadSet<RemittanceLine>()
            join rem in uow.ReadSet<Remittance>() on line.RemittanceId equals rem.Id
            where line.PayableId == payable.Id && line.DeletedAt == null && rem.DeletedAt == null
            select new { rem.GeneratedAt, rem.GeneratedBy, rem.FileName, rem.BankCode, line.Amount })
            .ToListAsync(ct);

        // The operator who generated the remittance is not among the actors the aggregate itself carries.
        guidActors.AddRange(remittances
            .Select(r => Guid.TryParse(r.GeneratedBy, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty));

        // Statement lines tied to any payment of this title (either linked to an existing baixa or settled here).
        var paymentIds = payable.Installments.SelectMany(i => i.Payments).Select(p => p.Id).ToList();
        var conciliations = paymentIds.Count == 0
            ? []
            : await uow.ReadSet<BankTransaction>()
                .Where(t => t.MatchedPaymentId != null && paymentIds.Contains(t.MatchedPaymentId!.Value))
                .Select(t => new ConciliationTrailEntry(t.CreatedAt, t.Date, t.Amount, t.PaymentPreexisting))
                .ToListAsync(ct);

        return new PayableTrailContext(
            reqCreatedAt, reqCreatedBy, autoLimit, accountCode, accountName,
            remittances
                .Select(r => new RemittanceTrailEntry(r.GeneratedAt, r.GeneratedBy, r.FileName, r.BankCode, r.Amount))
                .OrderBy(r => r.GeneratedAt)
                .ToList(),
            conciliations);
    }
}
