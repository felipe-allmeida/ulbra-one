using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A company's rule on when an accounting classification becomes mandatory. Turning either "
    + "flag on blocks the corresponding action for every unclassified payable of that company.")]
public sealed record ClassificationPolicyResponse(
    CompanyId CompanyId,
    [property: SwaggerSchema("Whether a payable must be classified before it can be approved.")]
    bool RequireToApprove,
    [property: SwaggerSchema("Whether a payable must be classified before it can be paid.")]
    bool RequireToPay)
{
    public static ClassificationPolicyResponse From(ClassificationPolicy p) =>
        new(new CompanyId(p.CompanyId), p.RequireToApprove, p.RequireToPay);
}
