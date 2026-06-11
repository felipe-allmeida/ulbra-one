using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ClassificationPolicyResponse(CompanyId CompanyId, bool RequireToApprove, bool RequireToPay)
{
    public static ClassificationPolicyResponse From(ClassificationPolicy p) =>
        new(new CompanyId(p.CompanyId), p.RequireToApprove, p.RequireToPay);
}
