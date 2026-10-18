using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ListPayments;

/// <summary>Query-string filters for the settlement history. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListPaymentsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company whose payments to list. Omit for the consolidated view.")]
    public CompanyId? CompanyId { get; set; }

    /// <summary>Inclusive lower bound on PaymentDate.</summary>
    [SwaggerSchema("Keep payments settled on or after this day.")]
    [SwaggerExample("2026-09-01")]
    public DateOnly? From { get; set; }

    /// <summary>Inclusive upper bound on PaymentDate.</summary>
    [SwaggerSchema("Keep payments settled on or before this day.")]
    [SwaggerExample("2026-09-30")]
    public DateOnly? To { get; set; }

    [SwaggerSchema("Bank account the money came out of.")]
    public BankAccountId? BankAccountId { get; set; }

    [SwaggerSchema("Method the payment was made with. Unlike the payable list, this is the method "
        + "recorded on the payment itself, not an effective fallback.")]
    public PaymentMethod? Method { get; set; }

    /// <summary>Matches counterparty name / document number (case-insensitive) and vendor CNPJ/CPF (digits).</summary>
    [SwaggerSchema("Free-text search over counterparty name, document number and vendor CNPJ/CPF.")]
    public string? Search { get; set; }

    /// <summary>Reversed baixas are hidden unless explicitly requested.</summary>
    [SwaggerSchema("Include payments that were reversed. They are HIDDEN by default, so a total taken "
        + "from this list without the flag is the net settled amount.")]
    public bool? IncludeReversed { get; set; }
}

/// <summary>Cross-payable settlement history (Histórico de baixas) — one row per payment.</summary>
public sealed class ListPayments : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("payments", Handle).WithName("ListPayments").WithTags("Finance")
            .WithSummary("Lists settled payments.")
            .WithDescription("Cross-payable settlement history (historico de baixas), one row per payment. Reversed "
                + "payments are hidden unless includeReversed is set.")
            .RequirePermission(Permissions.PayablesRead)
            .Produces<PaginatedResponse<PaymentListItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListPaymentsRequest req,
        UserContext user,
        CancellationToken ct)
    {
        // A present companyId always goes through the per-company opt-in filter (AEL-906): a management role
        // WITH a grant is restricted to it, so consolidated ≠ "reads any company". An omitted companyId
        // (consolidated "Todas") is allowed only for a consolidated role.
        if (req.CompanyId is { } companyId)
        {
            if (!user.CanAccess(companyId.Value))
                return ErrorResults.Forbidden("No access to this company.", ErrorCodes.Payment.CompanyForbidden);
        }
        else if (!user.CanSeeConsolidated)
        {
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payment.ConsolidatedForbidden);
        }

        var cid = req.CompanyId?.Value;
        var payables = PayableQueueFilters.ApplySearch(
            uow.ReadSet<Payable>().Where(p => cid == null || p.CompanyId == cid.Value), uow, req.Search);

        var rows = payables
            .SelectMany(p => p.Installments, (p, i) => new { Payable = p, Installment = i })
            .SelectMany(x => x.Installment.Payments, (x, pay) => new
            {
                PaymentId = pay.Id,
                PayableId = x.Payable.Id,
                InstallmentId = x.Installment.Id,
                InstallmentSequence = x.Installment.Sequence,
                InstallmentCount = x.Payable.Installments.Count,
                x.Payable.CounterpartyName,
                x.Payable.VendorId,
                x.Payable.DocumentNumber,
                pay.PaymentDate,
                pay.Amount,
                pay.Interest,
                pay.Fine,
                pay.Discount,
                pay.Method,
                pay.BankAccountId,
                pay.IsReversed,
                pay.ReversalReason,
                HasProof = pay.ProofStoragePath != null,
                pay.CreatedAt,
                pay.CreatedById,
            });

        if (req.From is { } from) rows = rows.Where(x => x.PaymentDate >= from);
        if (req.To is { } to) rows = rows.Where(x => x.PaymentDate <= to);
        if (req.BankAccountId is { } bid) rows = rows.Where(x => x.BankAccountId == bid.Value);
        if (req.Method is { } m) rows = rows.Where(x => x.Method == m);
        if (req.IncludeReversed is not true) rows = rows.Where(x => !x.IsReversed);

        var page = await rows
            .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.CreatedAt)
            .ToPaginatedAsync(req, ct);

        // One page-scoped Registry query for the vendor CNPJs (same batch pattern as ListPayables).
        var vendorIds = page.Items.Select(x => x.VendorId).Distinct().ToList();
        var documentByVendor = (await uow.ReadSet<VendorEntity>()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Document })
            .ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Document);

        return TypedResults.Ok(page.Map(x => new PaymentListItemResponse(
            new PaymentId(x.PaymentId), new PayableId(x.PayableId), new InstallmentId(x.InstallmentId),
            x.InstallmentSequence, x.InstallmentCount,
            x.CounterpartyName,
            documentByVendor.TryGetValue(x.VendorId, out var doc) ? doc : null,
            x.DocumentNumber,
            x.PaymentDate, x.Amount, x.Interest, x.Fine, x.Discount,
            x.Method, new BankAccountId(x.BankAccountId),
            x.IsReversed, x.ReversalReason, x.HasProof,
            x.CreatedAt, x.CreatedById)));
    }
}
