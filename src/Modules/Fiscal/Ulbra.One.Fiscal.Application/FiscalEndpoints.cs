using Microsoft.AspNetCore.Routing;

namespace Ulbra.One.Fiscal.Application;

public static class FiscalEndpoints
{
    public static IEndpointRouteBuilder MapFiscalEndpoints(this IEndpointRouteBuilder builder)
    {
        // Fiscal module endpoints (e.g., WithholdingRule CRUD) go here.
        return builder;
    }
}
