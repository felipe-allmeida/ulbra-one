using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class PingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/ping", () => Results.Ok(new
        {
            status = "ok",
            service = "ulbra-one",
            utc = DateTime.UtcNow
        }))
        .WithName("Ping")
        .WithTags("System");
    }
}
