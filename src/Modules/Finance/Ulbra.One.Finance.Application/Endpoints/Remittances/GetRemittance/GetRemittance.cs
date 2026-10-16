using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.GetRemittance;

public sealed class GetRemittance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle)
            .WithName("GetRemittance").WithTags("Finance")
            .WithSummary("Gets a remittance.")
            .WithDescription("Returns one remittance with its lines and the state of each: pending at the bank, paid or "
                + "returned.").RequirePermission(Permissions.TreasuryRead)
            .Produces<RemittanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(RemittanceId id, IUnitOfWork uow, CancellationToken ct)
    {
        // Lines are AutoIncluded by the EF configuration
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (remittance is null)
            return ErrorResults.NotFound("Remittance not found.", ErrorCodes.Remittance.NotFound);

        // Enrich each line with the underlying payable's unit/cost-center (they don't live on the line).
        // Loose cross-module reads (Core Unit/CostCenter) in the same physical DB, batched — no N+1.
        var payableIds = remittance.Lines.Select(l => l.PayableId).Distinct().ToList();
        var payables = await uow.ReadSet<Payable>()
            .Where(p => payableIds.Contains(p.Id))
            .Select(p => new { p.Id, p.OriginUnitId, p.CostCenterId })
            .ToListAsync(ct);

        var unitIds = payables.Where(p => p.OriginUnitId != null).Select(p => p.OriginUnitId!.Value).Distinct().ToList();
        var unitNames = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<Unit>().Where(u => unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);

        var ccIds = payables.Select(p => p.CostCenterId).Distinct().ToList();
        var ccNames = ccIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await uow.ReadSet<CostCenter>().Where(c => ccIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        var ctx = payables.ToDictionary(
            p => p.Id,
            p => new RemittanceLinePayableContext(
                p.OriginUnitId,
                p.OriginUnitId is { } u && unitNames.TryGetValue(u, out var un) ? un : null,
                p.CostCenterId,
                ccNames.TryGetValue(p.CostCenterId, out var cn) ? cn : null));

        return TypedResults.Ok(RemittanceResponse.From(remittance, ctx));
    }
}
