using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassificationSuggestion;

public sealed class ClassificationSuggestion : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/classification-suggestion", Handle).WithName("ClassificationSuggestion").WithTags("Finance")
            .WithSummary("Suggests a classification from the rules.")
            .WithDescription("Applies the configured classification policies to a payable and returns the expense account "
                + "they point at. Deterministic and free: it reads rules, calls nothing external, and suggests "
                + "only - classifying is a separate, explicit call.")
            .RequirePermission(Permissions.PayablesRead).Produces<ClassificationSuggestionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id,
        IUnitOfWork uow, CancellationToken ct)
    {
        var target = await uow.ReadSet<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (target is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var (acc, basedOn) = await VendorHistorySuggestion.FindAsync(uow, target, ct);
        return TypedResults.Ok(new ClassificationSuggestionResponse(acc, basedOn));
    }
}
