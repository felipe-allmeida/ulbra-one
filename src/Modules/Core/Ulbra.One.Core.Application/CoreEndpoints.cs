using Microsoft.AspNetCore.Routing;
using Ulbra.One.Core.Application.Endpoints;

namespace Ulbra.One.Core.Application;

public static class CoreEndpoints
{
    public static IEndpointRouteBuilder MapCoreEndpoints(this IEndpointRouteBuilder builder)
    {
        new PingEndpoint().Map(builder);
        new EmpresasEndpoint().Map(builder);
        return builder;
    }
}
