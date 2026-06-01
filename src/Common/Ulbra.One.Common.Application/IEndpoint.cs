using Microsoft.AspNetCore.Routing;

namespace Ulbra.One.Common.Application;

/// <summary>
/// Minimal API endpoint. Each feature exposes one <see cref="IEndpoint"/> that maps its routes.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder builder);
}
