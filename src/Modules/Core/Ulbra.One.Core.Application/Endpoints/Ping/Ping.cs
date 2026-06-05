using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Core.Application.Endpoints.Ping;

public sealed class Ping : IEndpoint
{
    public sealed record PingResponse(string Status, string Service, DateTime Utc);

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/ping", () => TypedResults.Ok(new PingResponse("ok", "ulbra-one", DateTime.UtcNow)))
            .WithName("Ping")
            .WithTags("System")
            .Produces<PingResponse>(StatusCodes.Status200OK);
    }
}
