using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Extensions;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapEndpoint<T>(this IEndpointRouteBuilder builder)
        where T : IEndpoint, new()
    {
        new T().Map(builder);
        return builder;
    }
}
