using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain.Errors;
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
            .WithName("SimulateRemittanceReturn").WithTags("Finance")
            .WithSummary("Builds a fake bank return file. Development only.")
            .WithDescription("Testing aid: synthesizes a CNAB return from the remittance\u0027s pending lines, marking "
                + "every one as paid, and hands it back as a file to feed into the import. Outside Development "
                + "it returns 404, and the check is in the handler - so the route exists in every "
                + "environment\u0027s spec and a 404 here means the environment refused. It generates a file; "
                + "it settles nothing on its own.").RequirePermission(Permissions.TreasuryRead)
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
            return ErrorResults.NotFound("Remittance not found or has no Pending lines.", ErrorCodes.Remittance.NotFound);

        var (bytes, fileName) = result.Value;
        return TypedResults.File(bytes, "text/plain", fileName);
    }
}
