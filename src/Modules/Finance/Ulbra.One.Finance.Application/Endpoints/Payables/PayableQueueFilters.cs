using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Endpoints.Payables.ListPayables;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>
/// Payable-level filters shared by the Contas a Pagar list and the payment-queue summary so the
/// queue's group headers always agree with its rows. Installment-level filters (dueFrom/dueTo/paid)
/// stay at the call sites, which project installments differently.
/// </summary>
public static class PayableQueueFilters
{
    /// <summary>
    /// Applies company/status/vendor/costCenter/unit/category/classified/search/payment-method
    /// filters. The payment-method filter matches the EFFECTIVE forma of any installment
    /// (<c>Installment.PaymentMethod ?? Payable.PaymentMethod</c>) — the same per-parcela resolution the
    /// remittance generation uses since AEL-933 (no account default anymore). Kept Task-returning so both
    /// call sites (list + queue summary) stay identical.
    /// </summary>
    public static Task<IQueryable<Payable>> ApplyAsync(
        IQueryable<Payable> payables, IUnitOfWork uow, ListPayablesRequest req, CancellationToken ct)
    {
        if (req.CompanyId is { } cid) payables = payables.Where(p => p.CompanyId == cid.Value);
        if (req.Status is { } st) payables = payables.Where(p => p.Status == st);
        if (req.VendorId is { } vid) payables = payables.Where(p => p.VendorId == vid.Value);
        if (req.CostCenterId is { } ccid) payables = payables.Where(p => p.CostCenterId == ccid.Value);
        if (req.UnitId is { } uid) payables = payables.Where(p => p.OriginUnitId == uid.Value);
        if (req.CategoryId is { } catid) payables = payables.Where(p => p.CategoryId == catid.Value);
        if (req.Classified is { } cl)
            payables = payables.Where(p => (p.AccountingAccountId != null) == cl);

        payables = ApplySearch(payables, uow, req.Search);

        if (req.PaymentMethod is { } pm)
            payables = payables.Where(p => p.Installments.Any(i => (i.PaymentMethod ?? p.PaymentMethod) == pm));

        return Task.FromResult(payables);
    }

    /// <summary>
    /// Free-text search shared by the queue and the payments history: case-insensitive on counterparty
    /// name / document number, and on the vendor document normalized the same way Registry stores it
    /// (alphanumeric CNPJ / digits CPF) — so a formatted or alphanumeric CNPJ still matches.
    /// </summary>
    public static IQueryable<Payable> ApplySearch(IQueryable<Payable> payables, IUnitOfWork uow, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return payables;

        var lowered = search.Trim().ToLower();
        var docTerm = DocumentValidator.NormalizeCnpj(search);
        var vendors = uow.ReadSet<VendorEntity>();
        return payables.Where(p =>
            p.CounterpartyName.ToLower().Contains(lowered)
            || (p.DocumentNumber != null && p.DocumentNumber.ToLower().Contains(lowered))
            || (docTerm != "" && vendors.Any(v => v.Id == p.VendorId && v.Document.Contains(docTerm))));
    }
}
