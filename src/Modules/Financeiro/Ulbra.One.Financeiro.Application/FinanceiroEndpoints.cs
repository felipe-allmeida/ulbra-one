using Microsoft.AspNetCore.Routing;
using Ulbra.One.Financeiro.Application.Endpoints;

namespace Ulbra.One.Financeiro.Application;

public static class FinanceiroEndpoints
{
    public static IEndpointRouteBuilder MapFinanceiroEndpoints(this IEndpointRouteBuilder builder)
    {
        new LancamentosEndpoint().Map(builder);
        return builder;
    }
}
