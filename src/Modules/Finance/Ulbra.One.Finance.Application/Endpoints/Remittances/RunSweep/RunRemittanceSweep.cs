using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.RunSweep;

public sealed class RunRemittanceSweep : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Date to run the sweep for. Omitted uses today.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly? AsOf);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("sweep", Handle)
            .WithName("RunRemittanceSweep").WithTags("Finance")
            .WithSummary("Runs the scheduled remittance sweep.")
            .WithDescription("Triggers the routine that generates remittances for everything due, on demand - the same job "
                + "the schedule runs. Returns what it produced.").RequirePermission(Permissions.TreasuryWrite)
            .Produces<RemittanceSweepResult>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        Request req, IRemittanceSweepService sweep, TimeProvider clock, CancellationToken ct)
    {
        var asOf = req.AsOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await sweep.SweepAsync(asOf, ct);
        return TypedResults.Ok(result);
    }
}
