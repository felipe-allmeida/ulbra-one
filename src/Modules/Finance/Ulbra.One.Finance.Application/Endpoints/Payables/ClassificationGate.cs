using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>Enforces the per-company <see cref="ClassificationPolicy"/> at the approve/pay gates.
/// Defaults off: when no policy row exists the action is always allowed.</summary>
internal static class ClassificationGate
{
    public static async Task<bool> RequiresButMissingAsync(IUnitOfWork uow, Payable payable, bool forPay, CancellationToken ct)
    {
        var policy = await uow.ReadSet<ClassificationPolicy>()
            .FirstOrDefaultAsync(p => p.CompanyId == payable.CompanyId, ct);
        if (policy is null) return false;
        var needed = forPay ? policy.RequireToPay : policy.RequireToApprove;
        return needed && !payable.IsClassified;
    }
}
