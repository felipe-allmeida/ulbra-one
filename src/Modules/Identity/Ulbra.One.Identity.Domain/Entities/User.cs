using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Identity.Domain.Entities;

/// <summary>User authenticated via Google Workspace. Roles: Admin | User.</summary>
public class User : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.User;
    public string? GoogleSubject { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Approval authority ceiling (alçada) for BudgetAdjustments (adequações). <c>null</c> = unlimited;
    /// a finite value blocks approving an adequação whose value exceeds it. Read cross-module via
    /// <c>IApprovalAuthority</c>. (Requisitions no longer use an alçada — Approver approves without limit.)</summary>
    public decimal? ApprovalLimit { get; private set; }

    /// <summary>Legacy flag from the old requisition auto-approval shortcut — no longer read for any decision
    /// (pending removal). Kept only so the wire contract / column don't churn ahead of a dedicated cleanup.</summary>
    public bool HasApprovalAutonomy { get; private set; }

    /// <summary>Per-user authorization to APPROVE budget adjustments (adequações), granted via the Users admin
    /// screen (AEL-883). The role <c>Controladoria</c> carries the <c>budget-adjustments:approve</c> permission,
    /// but only users with this flag may actually approve/reverse — read live so revocation is immediate.
    /// Default <c>false</c> (opt-in).</summary>
    public bool CanApproveBudgetAdjustment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private User() { }

    public User(string email, string name, string role)
    {
        Email = email;
        Name = name;
        Role = role;
    }

    public void RegisterGoogleLogin(string googleSubject, string name, DateTimeOffset at)
    {
        GoogleSubject = googleSubject;
        if (!string.IsNullOrWhiteSpace(name)) Name = name;
        LastLoginAt = at;
    }

    public void RegisterLogin(DateTimeOffset at) => LastLoginAt = at;

    public void SetRole(string role) => Role = role;

    /// <summary>Activates/deactivates the user (AEL-883). Inactive users are denied at login (token issuance).</summary>
    public void SetActive(bool active) => Active = active;

    /// <summary>Sets the approval authority (alçada + autonomy). A finite <paramref name="approvalLimit"/>
    /// must be non-negative; <c>null</c> means unlimited.</summary>
    public void SetApprovalAuthority(decimal? approvalLimit, bool hasApprovalAutonomy)
    {
        if (approvalLimit is < 0) throw new ArgumentOutOfRangeException(nameof(approvalLimit), "ApprovalLimit cannot be negative.");
        ApprovalLimit = approvalLimit;
        HasApprovalAutonomy = hasApprovalAutonomy;
    }

    /// <summary>Grants/revokes this user's authority to approve budget adjustments (AEL-883). Effective
    /// immediately (read live at approve time).</summary>
    public void SetCanApproveBudgetAdjustment(bool value) => CanApproveBudgetAdjustment = value;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
