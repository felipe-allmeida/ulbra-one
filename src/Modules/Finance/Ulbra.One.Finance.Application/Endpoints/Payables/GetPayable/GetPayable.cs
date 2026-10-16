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
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.GetPayable;

public sealed class GetPayable : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetPayable").WithTags("Finance")
            .WithSummary("Gets a payable.")
            .WithDescription("Returns a single payable with its installments, payments, attachments and "
                + "withholdings.")
            .RequirePermission(Permissions.PayablesRead).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, IUnitOfWork uow, IUserDirectory users, CancellationToken ct)
    {
        var p = await uow.ReadSet<Payable>()
            .Include(x => x.Attachments)
            .Include(x => x.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        // ── Resolve approver/rejecter display names (one query) ──────────────────────────────────
        var actors = new List<Guid>();
        if (Guid.TryParse(p.ApprovedBy, out var ag)) actors.Add(ag);
        if (Guid.TryParse(p.RejectedBy, out var rg)) actors.Add(rg);
        if (Guid.TryParse(p.AccountedBy, out var acg)) actors.Add(acg);
        var names = await users.GetNamesAsync(actors, ct);

        // ── Batch-resolve rail context (no per-row DB hits) ──────────────────────────────────────

        // 1. Default paying account for this company (one query).
        var defaultAccount = await uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.CompanyId == p.CompanyId && b.IsDefaultForPayments && b.DeletedAt == null, ct);

        // 2. Vendor bank info (one query by VendorId).
        var vendor = await uow.ReadSet<VendorEntity>()
            .FirstOrDefaultAsync(v => v.Id == p.VendorId, ct);
        var vendorBankInfo = vendor?.BankInfo ?? BankInfo.Empty;

        // 3. For InRemittance installments, batch-load owning remittance id + sequence via a single join.
        var inRemittanceIds = p.Installments
            .Where(i => i.Status == InstallmentStatus.InRemittance)
            .Select(i => i.Id)
            .ToList();

        Dictionary<Guid, (Guid RemittanceId, int Sequence)> remittanceByInstallment = new();
        if (inRemittanceIds.Count > 0)
        {
            var lines = await uow.ReadSet<RemittanceLine>()
                .Where(l => inRemittanceIds.Contains(l.InstallmentId) && l.DeletedAt == null)
                .Select(l => new { l.InstallmentId, l.RemittanceId, l.Sequence })
                .ToListAsync(ct);

            foreach (var line in lines)
                remittanceByInstallment[line.InstallmentId] = (line.RemittanceId, line.Sequence);
        }

        // 4. Build enriched installment responses.
        var installmentResponses = p.Installments
            .OrderBy(i => i.Sequence)
            .Select(i =>
            {
                RemittanceId? remittanceId = null;
                int? remittanceSequence = null;
                if (remittanceByInstallment.TryGetValue(i.Id, out var link))
                {
                    remittanceId = new RemittanceId(link.RemittanceId);
                    remittanceSequence = link.Sequence;
                }

                var ctx = new InstallmentRailContext(defaultAccount, vendorBankInfo, p.PaymentMethod, remittanceId, remittanceSequence);
                return InstallmentResponse.From(i, ctx);
            })
            .ToList();

        return TypedResults.Ok(PayableResponse.FromWithInstallments(p, installmentResponses, names));
    }
}
