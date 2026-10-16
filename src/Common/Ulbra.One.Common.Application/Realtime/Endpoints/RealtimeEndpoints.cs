using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

public static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder builder)
    {
        var realtime = builder.MapGroup("/realtime");
        realtime.MapEndpoint<StreamRealtime>();
        realtime.MapEndpoint<EnterPresence>();
        realtime.MapEndpoint<LeavePresence>();
        realtime.MapEndpoint<GetPresence>();
        return builder;
    }
}
