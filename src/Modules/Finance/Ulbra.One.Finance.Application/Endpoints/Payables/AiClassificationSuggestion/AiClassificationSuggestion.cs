using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

public sealed class AiClassificationSuggestion : IEndpoint
{
    private const double MinConfidence = 0.5;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/ai-classification-suggestion", Handle)
            .WithName("AiClassificationSuggestion").WithTags("Finance")
            .RequireAuthorization()
            .Produces<AiClassificationSuggestionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id, IAiCompletion ai, IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        // Analytical, payable-receivable accounts (chart of accounts is global reference data).
        var accounts = await uow.ReadSet<ChartAccount>()
            .Where(c => c.Kind == AccountKind.Analytical && c.IsActive && c.DeletedAt == null)
            .ToListAsync(ct);
        var accounting = accounts.Where(c => c.ChartType == ChartType.Accounting).ToList();
        var financial = accounts.Where(c => c.ChartType == ChartType.Financial).ToList();

        var prompt = AiClassificationPrompt.Build(
            payable.Description, payable.CounterpartyName, payable.DocumentNumber, payable.TotalAmount,
            accounting.Select(c => (c.Code, c.Name)).ToList(),
            financial.Select(c => (c.Code, c.Name)).ToList());

        var aiResult = await ai.CompleteAsync<PayableClassificationSuggestion>(prompt, ct: ct);
        if (aiResult.IsSuccess && aiResult.Value.Confidence >= MinConfidence)
        {
            var catalog = accounts.Select(c => new AiChartAccountRef(c.Id, c.Code, c.ChartType)).ToList();
            var (acc, fin) = AiClassificationResolver.Resolve(aiResult.Value, catalog);
            if (acc is not null || fin is not null)
                return TypedResults.Ok(new AiClassificationSuggestionResponse(
                    acc is { } a ? new ChartAccountId(a) : null,
                    fin is { } f ? new ChartAccountId(f) : null,
                    aiResult.Value.Confidence, aiResult.Value.Rationale, "ai"));
        }

        var (hAcc, hFin, _) = await VendorHistorySuggestion.FindAsync(uow, payable, ct);
        if (hAcc is null && hFin is null)
            return TypedResults.Ok(new AiClassificationSuggestionResponse(
                null, null, 0, "No suggestion available.", "none"));

        return TypedResults.Ok(new AiClassificationSuggestionResponse(
            hAcc, hFin, 0, "Based on prior classifications for this vendor.", "heuristic"));
    }
}
