using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

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
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            // Optimistic-concurrency loss (Postgres xmin row-version) → 409 Conflict.
            // Central mapping so every mutating endpoint is covered without per-endpoint try/catch.
            // Goes through ErrorResults like every other error, so it carries trace_id and error_code too.
            if (exception is DbUpdateConcurrencyException)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await ErrorResults.InvalidState(
                    "The record was modified by another operation; reload and retry.",
                    ErrorCodes.Concurrency.Conflict,
                    context.Request.Path).ExecuteAsync(context);
                return;
            }

            // Attach the exception to the request span so Azure Monitor "Failures" shows it (with
            // stack trace) nested under the failed request. ExceptionHandlerMiddleware already logs it
            // for the durable/searchable path; this adds the span-level request↔exception correlation.
            // The 409 branch above returns early — expected outcomes stay out of the exceptions table.
            if (exception is not null)
            {
                Activity.Current?.AddException(exception);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ErrorResults.InternalError(
                "An unexpected error occurred. If it persists, report the trace_id.",
                ErrorCodes.General.Unexpected,
                context.Request.Path).ExecuteAsync(context);
        }));
        app.UseStatusCodePages(); // emits problem+json for bare status codes like 404
        return app;
    }
}
