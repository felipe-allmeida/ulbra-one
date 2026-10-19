using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Managed, group-global expense category for payables (not the chart of accounts — that's a later slice).</summary>
public class ExpenseCategory : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private ExpenseCategory() { }
    public ExpenseCategory(string name) => Name = name.Trim();

    public void Rename(string name) => Name = name.Trim();
    public void Activate() => Active = true;
    public void Deactivate() => Active = false;
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
