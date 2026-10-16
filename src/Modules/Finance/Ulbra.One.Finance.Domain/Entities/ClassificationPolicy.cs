using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Per-company policy gating whether a payable must be classified (accounting + financial account)
/// before it can be approved and/or paid. Defaults off so existing flows are unaffected.</summary>
public sealed class ClassificationPolicy : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public bool RequireToApprove { get; private set; }
    public bool RequireToPay { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private ClassificationPolicy() { }

    public ClassificationPolicy(Guid companyId, bool requireToApprove = false, bool requireToPay = false)
    {
        CompanyId = companyId;
        RequireToApprove = requireToApprove;
        RequireToPay = requireToPay;
    }

    public void Update(bool requireToApprove, bool requireToPay)
    {
        RequireToApprove = requireToApprove;
        RequireToPay = requireToPay;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
