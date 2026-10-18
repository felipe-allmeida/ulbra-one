using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ulbra.One.Common.Infrastructure.Observability;

/// <summary>
/// Wires application observability: OpenTelemetry tracing + metrics and structured logging.
/// </summary>
/// <remarks>
/// Export is config-driven and a no-op when unconfigured — no background connections, no startup
/// dependency on any backend. Two independent, additive sinks:
/// <list type="bullet">
///   <item><b>OTLP</b> — registered only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (env) or
///   <c>Otlp:Endpoint</c> (config) is set. Point it at an OpenTelemetry Collector or the .NET Aspire
///   dashboard (OTLP gRPC <c>http://localhost:4317</c>, or HTTP/protobuf <c>http://localhost:4318</c>).</item>
///   <item><b>Azure Application Insights</b> — registered only when
///   <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set (Container Apps inject it via Terraform).
///   Ships the same traces, metrics and logs to Azure Monitor.</item>
/// </list>
/// </remarks>
public static class ObservabilityExtensions
{
    public const string ServiceName = "ulbra-one-api";

    public static WebApplicationBuilder AddAppObservability(this WebApplicationBuilder builder)
    {
        var environment = builder.Environment;

        // Logging: keep the default readable console in Development; emit structured JSON elsewhere.
        if (!environment.IsDevelopment())
        {
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }

        // Stamp the active trace/span id onto every log line so a log can be pivoted to its trace.
        builder.Logging.Configure(o => o.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

        // Resolve the OTLP endpoint (env var takes precedence over config). When absent, no exporter
        // is registered — no background connection attempts, no startup dependency on a collector.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? builder.Configuration["Otlp:Endpoint"];
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        // Azure Application Insights: ship the SAME OTel signals to Azure Monitor when a connection
        // string is present (Container Apps inject APPLICATIONINSIGHTS_CONNECTION_STRING via Terraform).
        // Absent (e.g. local dev without the var) → not registered, so no export and no coupling.
        var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? builder.Configuration["ApplicationInsights:ConnectionString"];
        var hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);

        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString()
            ?? "1.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: ServiceName,
                serviceVersion: serviceVersion))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    // Drop the outbox/inbox polling sweeps. They are ~99% of all database spans and
                    // carry no diagnostic value — see MassTransitTelemetryFilter for the numbers and
                    // for why the actual message delivery is deliberately kept.
                    .AddEntityFrameworkCoreInstrumentation(o =>
                        o.Filter = (_, command) => !MassTransitTelemetryFilter.IsOutboxPolling(command.CommandText))
                    .AddSource(Ai.AiTelemetry.SourceName);

                if (hasOtlp)
                {
                    t.AddOtlpExporter();
                }

                if (hasAppInsights)
                {
                    t.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnectionString);
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasOtlp)
                {
                    m.AddOtlpExporter();
                }

                if (hasAppInsights)
                {
                    m.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnectionString);
                }
            });

        // Logs → Azure Monitor (App Insights "traces" table). Registered only when the connection
        // string is present; otherwise logs stay on the console sinks configured above.
        if (hasAppInsights)
        {
            builder.Logging.AddOpenTelemetry(o =>
            {
                o.IncludeScopes = true;
                o.IncludeFormattedMessage = true;
                o.AddAzureMonitorLogExporter(x => x.ConnectionString = appInsightsConnectionString);
            });
        }

        return builder;
    }
}
