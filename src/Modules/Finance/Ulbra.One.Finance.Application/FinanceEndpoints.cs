using Microsoft.AspNetCore.Routing;
using Ulbra.One.Finance.Application.Endpoints;

namespace Ulbra.One.Finance.Application;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder builder)
    {
        new TransactionsEndpoint().Map(builder);
        return builder;
    }
}
