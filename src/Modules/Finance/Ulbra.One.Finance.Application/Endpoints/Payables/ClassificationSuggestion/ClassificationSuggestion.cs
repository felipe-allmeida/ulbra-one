using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassificationSuggestion;

public sealed class ClassificationSuggestion : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/classification-suggestion", Handle).WithName("ClassificationSuggestion").WithTags("Finance")
            .RequireAuthorization().Produces<ClassificationSuggestionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id,
        IUnitOfWork uow, CancellationToken ct)
    {
        var target = await uow.ReadSet<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (target is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var (acc, fin, basedOn) = await VendorHistorySuggestion.FindAsync(uow, target, ct);
        return TypedResults.Ok(new ClassificationSuggestionResponse(acc, fin, basedOn));
    }
}
