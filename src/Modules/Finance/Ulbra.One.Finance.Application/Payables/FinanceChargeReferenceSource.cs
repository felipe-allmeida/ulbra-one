using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Payables;

/// <summary>
/// Finance's answer to the duplicate-charge question: the references carried by installments of titles
/// that are still expected to be paid. A Cancelled or Rejected title releases its line — the boleto behind it
/// was never paid, and the vendor commonly re-issues the same slip against a new title.
/// </summary>
public sealed class FinanceChargeReferenceSource(IUnitOfWork uow) : IChargeReferenceSource
{
    private static readonly PayableStatus[] Closed = [PayableStatus.Cancelled, PayableStatus.Rejected];

    public async Task<ChargeReferenceUsage?> FindAsync(
        ChargeReferenceKind kind, IReadOnlyCollection<string> references,
        IReadOnlyCollection<Guid> excludeOwnerIds, CancellationToken ct)
    {
        // Installment carries no CompanyId of its own, so the payable is joined for both the status filter and
        // the reference shown to the user. The line is stored canonically (BoletoLine.Canonical), so this is an
        // index-friendly equality match rather than a normalize-in-SQL scan.
        var hit = await (
            from i in uow.ReadSet<Installment>()
            join p in uow.ReadSet<Payable>() on i.PayableId equals p.Id
            let value = kind == ChargeReferenceKind.BoletoLine ? i.BoletoDigitableLine : i.PixPayloadEmv
            where value != null
                  && references.Contains(value)
                  && !Closed.Contains(p.Status)
                  && !excludeOwnerIds.Contains(p.Id)
                  && (p.OriginRequisitionId == null || !excludeOwnerIds.Contains(p.OriginRequisitionId.Value))
            select new { Value = value!, p.DocumentNumber, p.CounterpartyName })
            .FirstOrDefaultAsync(ct);

        if (hit is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(hit.DocumentNumber)
            ? hit.CounterpartyName
            : $"{hit.DocumentNumber} ({hit.CounterpartyName})";
        return new ChargeReferenceUsage(hit.Value, "payable", reference);
    }
}
