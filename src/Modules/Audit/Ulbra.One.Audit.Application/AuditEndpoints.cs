using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Audit.Application.Endpoints.ListAuditLogs;

namespace Ulbra.One.Audit.Application;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder builder)
    {
        var audit = builder.MapGroup("/audit-logs");
        audit.MapEndpoint<ListAuditLogs>();
        return builder;
    }
}
