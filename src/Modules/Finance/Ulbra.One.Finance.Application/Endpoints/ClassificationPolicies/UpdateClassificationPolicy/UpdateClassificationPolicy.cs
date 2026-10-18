using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.ClassificationPolicies.UpdateClassificationPolicy;

public sealed class UpdateClassificationPolicy : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Block approval of a payable that has not been classified yet.")]
        bool RequireToApprove,
        [property: SwaggerSchema("Block payment of a payable that has not been classified yet.")]
        bool RequireToPay);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{companyId}", Handle).WithName("UpdateClassificationPolicy").WithTags("Finance")
            .WithSummary("Sets a company's classification policy.")
            .WithDescription("Decides at which points an unclassified payable is blocked: before "
                + "approval, before payment, both or neither. These are hard gates on the payable flow, "
                + "so turning one on can stop titles that were moving fine until now.")
            .RequirePermission(Permissions.PayablesWrite).Produces<ClassificationPolicyResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId, Request req,
        IUnitOfWork uow, CancellationToken ct)
    {
        var policy = await uow.Set<ClassificationPolicy>()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId.Value, ct);
        if (policy is null)
        {
            policy = new ClassificationPolicy(companyId.Value, req.RequireToApprove, req.RequireToPay);
            await uow.AddAsync(policy, ct);
        }
        else
        {
            policy.Update(req.RequireToApprove, req.RequireToPay);
        }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ClassificationPolicyResponse.From(policy));
    }
}
