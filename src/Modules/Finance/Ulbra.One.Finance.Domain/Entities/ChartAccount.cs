using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A node in a hierarchical chart of accounts (Accounting or Financial), used to classify payables.</summary>
public sealed class ChartAccount : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public ChartType ChartType { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public AccountKind Kind { get; private set; }
    public AccountNature? Nature { get; private set; }
    public bool IsActive { get; private set; } = true;

    // IAuditable
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    // ISoftDeletable
    public DateTimeOffset? DeletedAt { get; private set; }

    private ChartAccount() { }

    public ChartAccount(ChartType chartType, string code, string name, AccountKind kind,
        Guid? parentId = null, AccountNature? nature = null)
    {
        Code = code?.Trim() ?? throw new ArgumentNullException(nameof(code));
        Name = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (Code.Length == 0) throw new ArgumentException("Code required", nameof(code));
        if (Name.Length == 0) throw new ArgumentException("Name required", nameof(name));
        ChartType = chartType;
        Kind = kind;
        ParentId = parentId;
        Nature = nature;
    }

    public bool CanReceivePayable => Kind == AccountKind.Analytical && IsActive && DeletedAt is null;

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (trimmed.Length == 0) throw new ArgumentException("Name required", nameof(name));
        Name = trimmed;
    }

    public void Reparent(ChartAccount? parent)
    {
        if (parent is null) { ParentId = null; return; }
        if (parent.ChartType != ChartType) throw new InvalidOperationException("Parent must be same chart type.");
        if (parent.Kind != AccountKind.Synthetic) throw new InvalidOperationException("Parent must be a synthetic account.");
        if (parent.Id == Id) throw new InvalidOperationException("Account cannot be its own parent.");
        ParentId = parent.Id;
    }

    public void SetNature(AccountNature? nature) => Nature = nature;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
}
