using Microsoft.AspNetCore.Routing;

namespace Ulbra.One.Fiscal.Application;

public static class FiscalEndpoints
{
    public static IEndpointRouteBuilder MapFiscalEndpoints(this IEndpointRouteBuilder builder)
    {
        // Endpoints do módulo Fiscal (ex.: CRUD de WithholdingRule) entram aqui.
        return builder;
    }
}
