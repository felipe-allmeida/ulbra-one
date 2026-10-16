using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Application.Endpoints.WithholdingRules.DeleteWithholdingRule;

public sealed class DeleteWithholdingRule : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/{id}", Handle).WithName("DeleteWithholdingRule").WithTags("Fiscal")
            .WithSummary("Deletes a withholding rule.")
            .WithDescription("Soft-deletes the rule: the row is kept and stamped, so an assessment already "
                + "explained by it stays auditable, and it stops matching new withholdings. Deactivating "
                + "the rule has the same practical effect while keeping it listable with includeInactive.")
            .RequirePermission(Permissions.FiscalWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(WithholdingRuleId id, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var rule = await uow.Set<WithholdingRule>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (rule is null)
            return ErrorResults.NotFound("Withholding rule not found.", ErrorCodes.WithholdingRule.NotFound);

        rule.MarkDeleted(clock.GetUtcNow());
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
