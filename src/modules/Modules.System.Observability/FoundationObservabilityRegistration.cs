using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Modules.System.Observability;

/// <summary>
/// FR-18B: bounded, opt-in OTLP export; never requires a Collector to start or
/// handle a business request. No Identity or business DbContext dependency.
/// </summary>
public static class FoundationObservabilityRegistration
{
    public static IServiceCollection AddFoundationObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var settings = FoundationObservabilitySettings.Read(configuration, environment);

        // Opt-in private Tempo reader; never expose its destination to the
        // browser and never follow untrusted redirect or proxy configuration.
        services.AddHttpClient<FoundationTempoTraceReader>(http =>
            {
                http.Timeout = TimeSpan.FromSeconds(3);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Foundation-ControlTower/1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                MaxConnectionsPerServer = 8,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });


        // EF interceptors are resolved from the existing shared DbContext
        // registration. No coupling from Infrastructure to Identity/OTel.
        services.AddScoped<IDbCommandInterceptor, FoundationEfCommandInterceptor>();

        // Existing ILogger sinks remain unchanged. Activity tracking enables
        // TraceId/SpanId in providers that support scopes. Raw application logs
        // are NOT exported to OTLP until source redaction is separately audited.
        services.Configure<LoggerFactoryOptions>(options =>
        {
            options.ActivityTrackingOptions |=
                ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId;
        });

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: settings.ServiceName,
                serviceVersion: typeof(FoundationObservabilityRegistration)
                    .Assembly.GetName().Version?.ToString() ?? "unknown"))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(settings.TraceSampleRatio))
                    .AddSource(FoundationTelemetry.ActivitySourceName)
                    .AddSource("Foundation.Cqrs")
                    // Explicit raw SqlClient/Dapper wrappers only; EF has its own interceptor.
                    .AddSource("Foundation.SqlClient")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context =>
                            FoundationTelemetryPrivacy.CanTracePath(context.Request.Path.Value);
                        // Don't record raw exception message/stack trace into
                        // the OTel buffer. Failure state and sanitized type
                        // are emitted by FoundationTelemetryMiddleware.
                        options.RecordException = false;
                    })
                    .AddProcessor(new FoundationTelemetryPrivacy());

                if (settings.ExportEnabled)
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Protocol = OtlpExportProtocol.Grpc;
                        options.Endpoint = settings.CollectorEndpoint!;
                        options.TimeoutMilliseconds = 1000;
                        options.BatchExportProcessorOptions.MaxQueueSize = 512;
                        options.BatchExportProcessorOptions.MaxExportBatchSize = 64;
                        options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 5000;
                        options.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = 1000;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(FoundationTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation();

                if (settings.ExportEnabled)
                {
                    metrics.AddOtlpExporter((exporter, reader) =>
                    {
                        exporter.Protocol = OtlpExportProtocol.Grpc;
                        exporter.Endpoint = settings.CollectorEndpoint!;
                        exporter.TimeoutMilliseconds = 1000;
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000;
                        reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 1000;
                    });
                }
            });

        return services;
    }
}

public sealed record FoundationObservabilitySettings(
    string ServiceName, double TraceSampleRatio,
    bool ExportEnabled, Uri? CollectorEndpoint)
{
    public static FoundationObservabilitySettings Read(
        IConfiguration configuration, IHostEnvironment environment)
    {
        var service = configuration["Observability:ServiceName"]?.Trim();
        if (string.IsNullOrWhiteSpace(service)) service = "foundation-web";
        if (service.Length > 64 || !service.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new InvalidOperationException(
                "Observability:ServiceName must be a bounded code-owned service name.");

        var ratioText = configuration["Observability:TraceSampleRatio"] ?? "0.1";
        if (!double.TryParse(ratioText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var ratio) ||
            !double.IsFinite(ratio) || ratio < 0 || ratio > 1)
            throw new InvalidOperationException(
                "Observability:TraceSampleRatio must be between 0 and 1.");

        var enabled = configuration.GetValue<bool>("Observability:Otlp:Enabled");
        if (!enabled) return new(service, ratio, false, null);

        // Export can be enabled only with an explicit service-side endpoint.
        // Setting an arbitrary OTEL_EXPORTER_OTLP_ENDPOINT must not silently
        // switch on telemetry egress from an otherwise disabled Foundation.
        var rawEndpoint = configuration["Observability:Otlp:Endpoint"];
        if (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var endpoint) ||
            !string.IsNullOrWhiteSpace(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/" ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
                !(environment.IsDevelopment() && endpoint.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException(
                "Enabled Observability:Otlp:Endpoint requires an HTTPS gRPC Collector authority (HTTP only in Development), without credentials or query.");
        }

        return new(service, ratio, true, endpoint);
    }
}
