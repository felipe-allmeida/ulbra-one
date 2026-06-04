using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Common.Infrastructure.ErrorHandling;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddAppProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ctx =>
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path);

    /// <summary>Converts unhandled exceptions into RFC 7807 responses (no stack trace).</summary>
    public static IApplicationBuilder UseAppExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(builder => builder.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await TypedResults.Problem(
                title: "Erro interno",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: context.Request.Path).ExecuteAsync(context);
        }));
        app.UseStatusCodePages(); // emits problem+json for bare status codes like 404
        return app;
    }
}
