using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>A node in the global double-entry chart of accounts (plano de contas contábil).
/// The chart is shared by all companies; bookkeeping stays per company via <see cref="JournalEntry.CompanyId"/>.
/// Synthetic accounts form the hierarchy; only active analytical accounts receive journal lines.</summary>
public sealed class LedgerAccount : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AccountGroup AccountGroup { get; private set; }
    public NormalBalance NormalBalance { get; private set; }
    public LedgerAccountKind Kind { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; } = true;

    // IAuditable
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    // ISoftDeletable
    public DateTimeOffset? DeletedAt { get; private set; }

    private LedgerAccount() { }

    public LedgerAccount(string code, string name, AccountGroup accountGroup,
        LedgerAccountKind kind, Guid? parentId = null, NormalBalance? normalBalance = null)
    {
        Code = NormalizeRequired(code, nameof(code));
        Name = NormalizeRequired(name, nameof(name));
        AccountGroup = accountGroup;
        Kind = kind;
        ParentId = parentId;
        NormalBalance = normalBalance ?? DefaultNormalBalance(accountGroup);
    }

    /// <summary>The natural balance side for an account group (Asset/Expense → Debit; otherwise Credit).</summary>
    public static NormalBalance DefaultNormalBalance(AccountGroup group) =>
        group is AccountGroup.Asset or AccountGroup.Expense ? NormalBalance.Debit : NormalBalance.Credit;

    /// <summary>True when this account may receive a journal line: analytical, active and not soft-deleted.</summary>
    public bool CanReceiveEntry => Kind == LedgerAccountKind.Analytical && IsActive && DeletedAt is null;

    /// <summary>Updates the editable attributes (group/natureza/kind). Reparenting is a separate operation.</summary>
    public void Update(string name, AccountGroup accountGroup, LedgerAccountKind kind, NormalBalance normalBalance)
    {
        Name = NormalizeRequired(name, nameof(name));
        AccountGroup = accountGroup;
        Kind = kind;
        NormalBalance = normalBalance;
    }

    public void Rename(string name) => Name = NormalizeRequired(name, nameof(name));

    /// <summary>Reparents under a synthetic account of the same group (or clears the parent when null).</summary>
    public void Reparent(LedgerAccount? parent)
    {
        if (parent is null) { ParentId = null; return; }
        if (parent.Id == Id) throw new InvalidOperationException("Account cannot be its own parent.");
        if (parent.Kind != LedgerAccountKind.Synthetic) throw new InvalidOperationException("Parent must be a synthetic account.");
        if (parent.AccountGroup != AccountGroup) throw new InvalidOperationException("Parent must belong to the same account group.");
        ParentId = parent.Id;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;

    private static string NormalizeRequired(string value, string paramName)
    {
        var trimmed = value?.Trim() ?? throw new ArgumentNullException(paramName);
        if (trimmed.Length == 0) throw new ArgumentException($"{paramName} required", paramName);
        return trimmed;
    }
}
