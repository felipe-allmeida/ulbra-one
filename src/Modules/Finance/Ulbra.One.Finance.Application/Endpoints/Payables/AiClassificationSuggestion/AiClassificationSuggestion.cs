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
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

public sealed class AiClassificationSuggestion : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/ai-classification-suggestion", Handle)
            .WithName("AiClassificationSuggestion").WithTags("Finance")
            .WithSummary("Suggests a classification using AI.")
            .WithDescription("Asks the AI classifier for an expense account when the deterministic policies do not resolve "
                + "one. It is a POST because it calls an external model and costs money per call, not because "
                + "it changes anything - the payable is untouched, and applying the suggestion is a separate "
                + "classify call.")
            .RequirePermission(Permissions.PayablesRead)
            .Produces<AiClassificationSuggestionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id, IPayableClassifier classifier, IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var result = await classifier.ClassifyAsync(payable, ct);

        return TypedResults.Ok(new AiClassificationSuggestionResponse(
            result.AccountingAccountId is { } acc ? new LedgerAccountId(acc) : null,
            result.Confidence,
            result.Rationale ?? string.Empty,
            result.Source));
    }
}
