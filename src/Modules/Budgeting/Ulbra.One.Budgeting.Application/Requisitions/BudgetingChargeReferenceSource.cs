using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;

namespace Ulbra.One.Budgeting.Application.Requisitions;

/// <summary>
/// Budgeting's answer to the duplicate-charge question: the references authored on requisition payment
/// plans and on Conta Fixa cycles. These are boletos already headed for payment even before Finance
/// materializes the title, which is exactly why the check cannot live in Finance alone. A cancelled
/// requisition releases its lines.
/// </summary>
public sealed class BudgetingChargeReferenceSource(IUnitOfWork uow) : IChargeReferenceSource
{
    public async Task<ChargeReferenceUsage?> FindAsync(
        ChargeReferenceKind kind, IReadOnlyCollection<string> references,
        IReadOnlyCollection<Guid> excludeOwnerIds, CancellationToken ct)
    {
        var fromPlan = await (
            from i in uow.ReadSet<RequisitionInstallment>()
            join r in uow.ReadSet<Requisition>() on i.RequisitionId equals r.Id
            let value = kind == ChargeReferenceKind.BoletoLine ? i.BoletoDigitableLine : i.PixPayloadEmv
            where value != null
                  && references.Contains(value)
                  && r.Status != RequisitionStatus.Cancelled
                  && !excludeOwnerIds.Contains(r.Id)
            select new { Value = value!, r.Code })
            .FirstOrDefaultAsync(ct);

        if (fromPlan is not null)
            return new ChargeReferenceUsage(fromPlan.Value, "requisition", $"Solicitação {fromPlan.Code}");

        // A Conta Fixa cycle links its own Payable, so an edit of that title must not collide with the cycle
        // that produced it — hence the PayableId exclusion alongside the requisition's.
        var fromCycle = await (
            from c in uow.ReadSet<RequisitionCycle>()
            join r in uow.ReadSet<Requisition>() on c.RequisitionId equals r.Id
            let value = kind == ChargeReferenceKind.BoletoLine ? c.BoletoDigitableLine : c.PixPayloadEmv
            where value != null
                  && references.Contains(value)
                  && r.Status != RequisitionStatus.Cancelled
                  && !excludeOwnerIds.Contains(r.Id)
                  && (c.PayableId == null || !excludeOwnerIds.Contains(c.PayableId.Value))
            select new { Value = value!, r.Code, c.Competency })
            .FirstOrDefaultAsync(ct);

        return fromCycle is null
            ? null
            : new ChargeReferenceUsage(fromCycle.Value, "requisition", $"Solicitação {fromCycle.Code} ({fromCycle.Competency})");
    }
}
