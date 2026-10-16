using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Infrastructure.ErrorHandling;

namespace Ulbra.One.IntegrationTests.Observability;

/// <summary>
/// The global exception handler must attach unhandled exceptions to the request Activity — otherwise
/// App Insights "Failures" shows a bare 500 with no exception nested under the request. (The framework's
/// <c>ExceptionHandlerMiddleware</c> already logs the exception, so the durable log path is covered; the
/// span event is the missing piece for the request↔exception correlation in the transaction view.)
/// </summary>
public sealed class ExceptionTelemetryTests
{
    [Fact]
    public async Task UnhandledException_IsRecordedOnRequestActivity()
    {
        // Thread-safe: this listener is global (ShouldListenTo = _ => true), so activities stopped by other tests
        // running in parallel are appended from their threads too.
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var app = BuildApp();
        await app.StartAsync();
        using (var client = app.GetTestClient())
        {
            var response = await client.GetAsync("/boom");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        await app.StopAsync(); // deterministically flush the stopped request Activity

        // Snapshot before asserting so the enumeration can't race a concurrent Add from a parallel test.
        var snapshot = stopped.ToArray();
        var requestActivity = Assert.Single(
            snapshot, a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn");
        var exceptionEvent = Assert.Single(requestActivity.Events, e => e.Name == "exception");
        Assert.Contains(exceptionEvent.Tags, t => t.Key == "exception.stacktrace");
        Assert.Contains(exceptionEvent.Tags,
            t => t.Key == "exception.message" && (t.Value as string) == "boom");
        Assert.Equal(ActivityStatusCode.Error, requestActivity.Status);
    }

    [Fact]
    public async Task ExpectedConflict_IsNotRecordedAsException()
    {
        // Thread-safe: this listener is global (ShouldListenTo = _ => true), so activities stopped by other tests
        // running in parallel are appended from their threads too.
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var app = BuildApp(mapEndpoints: app =>
            app.MapGet("/conflict", void () =>
                throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("row changed")));
        await app.StartAsync();
        using (var client = app.GetTestClient())
        {
            var response = await client.GetAsync("/conflict");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        await app.StopAsync();

        // Snapshot before asserting so the enumeration can't race a concurrent Add from a parallel test.
        var snapshot = stopped.ToArray();
        var requestActivity = Assert.Single(
            snapshot, a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn");
        // A concurrency conflict is an expected outcome (409), not a fault — it must not pollute Failures.
        Assert.DoesNotContain(requestActivity.Events, e => e.Name == "exception");
    }

    private static WebApplication BuildApp(Action<WebApplication>? mapEndpoints = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseAppExceptionHandler();
        if (mapEndpoints is null)
        {
            app.MapGet("/boom", void () => throw new InvalidOperationException("boom"));
        }
        else
        {
            mapEndpoints(app);
        }

        return app;
    }
}
