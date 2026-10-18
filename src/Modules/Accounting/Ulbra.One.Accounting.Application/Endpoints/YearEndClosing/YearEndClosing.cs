using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.YearEndClosing;

/// <summary>Encerramento anual: zeroes the exercise's result accounts into Apuração, transfers the net result
/// to Lucros/Prejuízos Acumulados (Origin = Closing, competência 31/12) and closes the fiscal year. Idempotent.</summary>
public sealed class YearEndClosing : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company whose exercise is being closed.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Fiscal year to close.")]
        [SwaggerExample("2026")]
        public int FiscalYear { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("YearEndClosing").WithTags("Accounting")
            .WithSummary("Closes the fiscal year.")
            .WithDescription("Encerramento anual, in one operation: zeroes the exercise's result accounts "
                + "into Apuração, transfers the net result to Lucros/Prejuízos Acumulados as an entry "
                + "stamped 31/12 of the year, and closes the fiscal year. Idempotent — running it again on "
                + "a closed year changes nothing rather than duplicating the closing entries.")
            .RequirePermission(Permissions.AccountingPost)
            .Produces<YearEndClosingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        Request req, IYearEndClosingService closing, IUnitOfWork uow, CancellationToken ct)
    {
        var result = await closing.CloseYearAsync(req.CompanyId.Value, req.FiscalYear, ct);
        if (result.IsFailure)
            return ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(YearEndClosingResponse.From(req.FiscalYear, result.Value));
    }
}
