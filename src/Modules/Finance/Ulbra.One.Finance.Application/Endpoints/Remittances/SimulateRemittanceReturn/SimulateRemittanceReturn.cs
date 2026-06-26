using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.SimulateRemittanceReturn;

/// <summary>
/// <c>POST /remittances/{id}/simulate-return</c> — <b>Development-only</b>.
/// Builds a synthetic CNAB240 return file (.RET) from the remittance's Pending lines,
/// marking each as paid (occurrence code "BD"), and returns it as a downloadable file.
/// Returns 404 when the application is not running in Development mode.
/// </summary>
public sealed class SimulateRemittanceReturn : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/simulate-return", Handle)
            .WithName("SimulateRemittanceReturn").WithTags("Finance").RequireAuthorization()
            .Produces<byte[]>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        IHostEnvironment env,
        IRemittanceReturnSimulator simulator,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return TypedResults.NotFound();

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await simulator.SimulateAsync(id.Value, today, ct);

        if (result is null)
            return TypedResults.Problem(
                detail: "Remittance not found or has no Pending lines.",
                statusCode: 404,
                title: "Remittance.NotFound");

        var (bytes, fileName) = result.Value;
        return TypedResults.File(bytes, "text/plain", fileName);
    }
}
