using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ulbra.One.Common.Infrastructure.Observability;

/// <summary>
/// Wires application observability: OpenTelemetry tracing + metrics and structured logging.
/// </summary>
/// <remarks>
/// Tracing/metrics export is config-driven and a no-op without a collector: the OTLP exporter is
/// only registered when an endpoint is configured via the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
/// environment variable or the <c>Otlp:Endpoint</c> config key. Point it at an OpenTelemetry
/// Collector or the .NET Aspire dashboard (default OTLP gRPC port <c>http://localhost:4317</c>,
/// or HTTP/protobuf on <c>http://localhost:4318</c>) to ship telemetry.
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

        // Resolve the OTLP endpoint (env var takes precedence over config). When absent, no exporter
        // is registered — no background connection attempts, no startup dependency on a collector.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? builder.Configuration["Otlp:Endpoint"];
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

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
                    .AddEntityFrameworkCoreInstrumentation();

                if (hasOtlp)
                {
                    t.AddOtlpExporter();
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
            });

        return builder;
    }
}
