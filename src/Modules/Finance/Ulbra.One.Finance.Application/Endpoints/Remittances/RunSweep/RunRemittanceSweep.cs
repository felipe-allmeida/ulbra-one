using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.RunSweep;

public sealed class RunRemittanceSweep : IEndpoint
{
    public sealed record Request(DateOnly? AsOf);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("sweep", Handle)
            .WithName("RunRemittanceSweep").WithTags("Finance").RequireAuthorization()
            .Produces<RemittanceSweepResult>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        Request req, IRemittanceSweepService sweep, TimeProvider clock, CancellationToken ct)
    {
        var asOf = req.AsOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await sweep.SweepAsync(asOf, ct);
        return TypedResults.Ok(result);
    }
}
