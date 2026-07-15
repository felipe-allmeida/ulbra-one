using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ClassificationPolicies.GetClassificationPolicy;

public sealed class GetClassificationPolicy : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{companyId}", Handle).WithName("GetClassificationPolicy").WithTags("Finance")
            .RequirePermission(Permissions.PayablesRead).Produces<ClassificationPolicyResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId,
        IUnitOfWork uow, CancellationToken ct)
    {
        var policy = await uow.ReadSet<ClassificationPolicy>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId.Value, ct);
        return TypedResults.Ok(policy is null
            ? new ClassificationPolicyResponse(companyId, false, false)
            : ClassificationPolicyResponse.From(policy));
    }
}
