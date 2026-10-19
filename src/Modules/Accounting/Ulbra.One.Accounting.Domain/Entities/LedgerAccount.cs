using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>A node in the global double-entry chart of accounts (plano de contas contábil).
/// The chart is shared by all companies; bookkeeping stays per company via <see cref="JournalEntry.CompanyId"/>.
/// Synthetic accounts form the hierarchy; only active analytical accounts receive journal lines.</summary>
public sealed class LedgerAccount : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Code { get; private set; } = string.Empty;
    /// <summary>Optional short numeric alias ("código reduzido") used for quick data entry; unique when present.</summary>
    public string? ReducedCode { get; private set; }
    /// <summary>The chart's "Natureza" classification (1–5), taken verbatim from the sheet; independent of the
    /// account group and of <see cref="NormalBalance"/> (which carries the débito/crédito).</summary>
    public int? Natureza { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public AccountGroup AccountGroup { get; private set; }
    public NormalBalance NormalBalance { get; private set; }
    public LedgerAccountKind Kind { get; private set; }
    public Guid? ParentId { get; private set; }
    /// <summary>The structured-DRE line this account rolls up to (null on non-result accounts). Defaulted from
    /// the chart at seed time and editable in the ledger-account CRUD.</summary>
    public DreLine? DreLine { get; private set; }
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
        LedgerAccountKind kind, Guid? parentId = null, NormalBalance? normalBalance = null,
        string? reducedCode = null, int? natureza = null, DreLine? dreLine = null)
    {
        Code = NormalizeRequired(code, nameof(code));
        Name = NormalizeRequired(name, nameof(name));
        AccountGroup = accountGroup;
        Kind = kind;
        ParentId = parentId;
        NormalBalance = normalBalance ?? DefaultNormalBalance(accountGroup);
        ReducedCode = NormalizeOptional(reducedCode);
        Natureza = natureza;
        DreLine = dreLine;
    }

    /// <summary>The natural balance side for an account group (Asset/Expense → Debit; otherwise Credit).</summary>
    public static NormalBalance DefaultNormalBalance(AccountGroup group) =>
        group is AccountGroup.Asset or AccountGroup.Expense ? NormalBalance.Debit : NormalBalance.Credit;

    /// <summary>True when this account may receive a journal line: analytical, active and not soft-deleted.</summary>
    public bool CanReceiveEntry => Kind == LedgerAccountKind.Analytical && IsActive && DeletedAt is null;

    /// <summary>Updates the editable attributes (group/normal balance/kind/reduced code/natureza/DRE line). Reparenting is a separate operation.</summary>
    public void Update(string name, AccountGroup accountGroup, LedgerAccountKind kind, NormalBalance normalBalance,
        string? reducedCode = null, int? natureza = null, DreLine? dreLine = null)
    {
        Name = NormalizeRequired(name, nameof(name));
        AccountGroup = accountGroup;
        Kind = kind;
        NormalBalance = normalBalance;
        ReducedCode = NormalizeOptional(reducedCode);
        Natureza = natureza;
        DreLine = dreLine;
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

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
