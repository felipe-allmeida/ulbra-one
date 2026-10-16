namespace Ulbra.One.Common.Application.Authentication;

/// <summary>A user's approval authority. <see cref="ApprovalLimit"/> is <c>null</c> when unlimited (the alçada).
/// <see cref="CanApproveBudgetAdjustment"/> is the per-user grant of authority to approve/reverse budget
/// adjustments (AEL-883): a Controladoria user only approves when it is <c>true</c>.</summary>
public sealed record ApprovalAuthority(decimal? ApprovalLimit, bool HasApprovalAutonomy, bool CanApproveBudgetAdjustment);

/// <summary>
/// Resolves a user's approval authority (limit) by id, without callers referencing the Identity module's
/// domain. Implemented in Identity.Infrastructure; consumed by the BudgetAdjustment (adequação) approval flow
/// (Budgeting) — the requisition flow no longer uses an alçada (Approver has unlimited authority).
/// Mirrors <see cref="IUserDirectory"/>.
/// </summary>
public interface IApprovalAuthority
{
    /// <summary>Returns the user's authority, or <c>null</c> when the user id is unknown.</summary>
    Task<ApprovalAuthority?> GetAsync(Guid userId, CancellationToken ct);
}
