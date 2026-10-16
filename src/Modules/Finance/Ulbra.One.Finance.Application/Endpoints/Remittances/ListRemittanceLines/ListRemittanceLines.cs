using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ListRemittanceLines;

/// <summary>Query-string filters for the flat títulos list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListRemittanceLinesRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the remittances. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Keep only títulos of payables tagged with this unit (Unidade).")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Keep only títulos of payables charged to this cost center (Natureza).")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Keep only títulos in remittances of this payment form (Pix/Ted/BoletoBancario/Convenio).")]
    public RemittanceCategory? Category { get; set; }

    [SwaggerSchema("Keep only títulos in this bank state (Pending/Paid/Returned).")]
    public RemittanceLineStatus? Status { get; set; }

    [SwaggerSchema("Keep títulos whose remittance was generated on or after this day.")]
    [SwaggerExample("2026-08-01")]
    public DateOnly? From { get; set; }

    [SwaggerSchema("Keep títulos whose remittance was generated on or before this day.")]
    [SwaggerExample("2026-08-31")]
    public DateOnly? To { get; set; }

    [SwaggerSchema("Keep only títulos of this remittance.")]
    public RemittanceId? RemittanceId { get; set; }
}

/// <summary>
/// Flat list of remittance títulos across every remittance (one row per line), joining each line to its
/// remittance and payable so it can be filtered by unidade/natureza/forma/status — the data behind the
/// "Remessas / Títulos" tab's unit filter.
/// </summary>
public sealed class ListRemittanceLines : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/lines", Handle).WithName("ListRemittanceLines").WithTags("Finance")
            .WithSummary("Lists remittance títulos across remittances.")
            .WithDescription("One row per remittance line, joined to its remittance and payable, so the "
                + "títulos generated into CNAB files can be browsed and filtered by unidade, natureza, forma, "
                + "status and período — across every remittance, not just one.")
            .RequirePermission(Permissions.TreasuryRead)
            .Produces<PaginatedResponse<RemittanceLineListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListRemittanceLinesRequest req,
        CancellationToken ct)
    {
        var q =
            from l in uow.ReadSet<RemittanceLine>()
            where l.DeletedAt == null
            join r in uow.ReadSet<Remittance>() on l.RemittanceId equals r.Id
            join p in uow.ReadSet<Payable>() on l.PayableId equals p.Id
            select new { l, r, p };

        if (req.CompanyId is { } cid) q = q.Where(x => x.r.CompanyId == cid.Value);
        if (req.RemittanceId is { } rid) q = q.Where(x => x.l.RemittanceId == rid.Value);
        if (req.Category is { } cat) q = q.Where(x => x.r.Category == cat);
        if (req.Status is { } st) q = q.Where(x => x.l.Status == st);
        if (req.UnitId is { } uid) q = q.Where(x => x.p.OriginUnitId == uid.Value);
        if (req.CostCenterId is { } ccid) q = q.Where(x => x.p.CostCenterId == ccid.Value);
        if (req.From is { } from)
        {
            var f = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(x => x.r.GeneratedAt >= f);
        }
        if (req.To is { } to)
        {
            var t = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(x => x.r.GeneratedAt < t);
        }

        // Project to primitives so the query + pagination translate to SQL; names are resolved after.
        var page = await q
            .OrderByDescending(x => x.r.GeneratedAt).ThenBy(x => x.l.Sequence)
            .Select(x => new
            {
                x.l.Id,
                x.l.RemittanceId,
                RemittanceSequence = x.r.SequenceNumber,
                x.r.Category,
                x.r.GeneratedAt,
                x.l.PayableId,
                x.l.InstallmentId,
                x.p.OriginRequisitionCode,
                x.l.FavoredName,
                x.p.OriginUnitId,
                x.p.CostCenterId,
                x.l.Method,
                x.l.Amount,
                x.l.DueDate,
                x.l.Status,
                x.l.EffectiveDate,
                x.l.ReturnReason,
            })
            .ToPaginatedAsync(req, ct);

        // Batch-resolve unit + cost-center display names for the page (no per-row DB hits).
        var unitIds = page.Items.Where(x => x.OriginUnitId != null).Select(x => x.OriginUnitId!.Value).Distinct().ToList();
        var unitNames = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<Unit>().Where(u => unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);

        var ccIds = page.Items.Select(x => x.CostCenterId).Distinct().ToList();
        var ccNames = ccIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<CostCenter>().Where(c => ccIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        return TypedResults.Ok(page.Map(x => new RemittanceLineListItemResponse(
            x.Id, new RemittanceId(x.RemittanceId), x.RemittanceSequence, x.Category?.ToString(), x.GeneratedAt,
            new PayableId(x.PayableId), new InstallmentId(x.InstallmentId), x.OriginRequisitionCode, x.FavoredName,
            x.OriginUnitId is { } u ? new UnitId(u) : null,
            x.OriginUnitId is { } u2 && unitNames.TryGetValue(u2, out var un) ? un : null,
            new CostCenterId(x.CostCenterId), ccNames.TryGetValue(x.CostCenterId, out var cn) ? cn : null,
            x.Method.ToString(), x.Amount, x.DueDate, x.Status.ToString(), x.EffectiveDate, x.ReturnReason)));
    }
}
