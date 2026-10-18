using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure;

/// <summary>Reads a user's approval authority (alçada + autonomy) from the single AppDbContext via the
/// shared unit of work — the Identity-side adapter for <see cref="IApprovalAuthority"/>.</summary>
public sealed class ApprovalAuthorityDirectory(IUnitOfWork uow) : IApprovalAuthority
{
    public async Task<ApprovalAuthority?> GetAsync(Guid userId, CancellationToken ct) =>
        await uow.ReadSet<User>()
            .Where(u => u.Id == userId)
            .Select(u => new ApprovalAuthority(u.ApprovalLimit, u.HasApprovalAutonomy, u.CanApproveBudgetAdjustment))
            .FirstOrDefaultAsync(ct);
}
