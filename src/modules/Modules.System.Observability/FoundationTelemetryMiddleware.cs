using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Modules.System.Observability;

/// <summary>
/// Safe correlation boundary. Does not inspect request/response bodies,
/// untrusted headers, query strings, credentials or SQL values.
/// Must run before Identity, BFF and business endpoints without reordering
/// their authorization, tenant isolation or antiforgery middleware.
/// </summary>
public sealed class FoundationTelemetryMiddleware(
    RequestDelegate next,
    ILogger<FoundationTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // HTTP Activity is created by ASP.NET Core/OTel before middleware.
        var rootActivity = Activity.Current;
        var traceId = rootActivity?.TraceId.ToString();
        var capture = FoundationTelemetryPrivacy.CanTracePath(context.Request.Path.Value);

        // W3C baggage comes from an untrusted request. Do not forward user
        // supplied TenantId/Role/DiagnosticSession as trusted metadata.
        if (rootActivity is not null)
            foreach (var item in rootActivity.Baggage.ToArray())
                rootActivity.SetBaggage(item.Key, null);

        if (!string.IsNullOrEmpty(traceId) && capture)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Trace-Id"] = traceId;
                return Task.CompletedTask;
            });
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = traceId ?? "unavailable",
            ["SpanId"] = rootActivity?.SpanId.ToString() ?? "unavailable"
        });

        using var span = capture
            ? FoundationTelemetry.ActivitySource.StartActivity(
                "foundation.http.pipeline", ActivityKind.Internal)
            : null;

        var startTimestamp = Stopwatch.GetTimestamp();
        var threw = false;
        try
        {
            await next(context);
        }
        catch (Exception error)
        {
            threw = true;
            span?.SetStatus(ActivityStatusCode.Error);
            // Exception messages / stack traces can contain password,
            // session data, SQL parameter values or personal information.
            // Record only a stable type identifier and preserve the original
            // exception/response behavior by rethrowing unchanged.
            var exceptionType = error.GetType().Name;
            span?.SetTag("foundation.error.type", exceptionType);
            logger.LogError(
                "Request pipeline failed; exception type {ExceptionType} trace {TraceId}",
                exceptionType, traceId ?? "unavailable");
            throw;
        }
        finally
        {
            if (capture)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                var status = threw ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                var method = FoundationTelemetryPrivacy.NormalizeMethod(context.Request.Method);
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "<unmatched>";
                var statusClass = status is >= 100 and <= 599 ? $"{status / 100}xx" : "other";
                // No Tenant/User/Trace ID in metric labels.
                var tags = new TagList
                {
                    { "http.request.method", method },
                    { "http.response.status_class", statusClass }
                };
                FoundationTelemetry.CompletedRequests.Add(1, tags);
                FoundationTelemetry.PipelineDuration.Record(elapsedMs, tags);

                span?.SetTag("http.route", route);
                span?.SetTag("http.response.status_code", status);
                if (status >= 500)
                    span?.SetStatus(ActivityStatusCode.Error);

                if (status >= 500)
                    logger.LogWarning("HTTP request failed: {Method} {Route} {StatusCode} {DurationMs} ms",
                        method, route, status, elapsedMs);
                else if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("HTTP request: {Method} {Route} {StatusCode} {DurationMs} ms",
                        method, route, status, elapsedMs);
            }
        }
    }
}

public static class FoundationTelemetryApplicationExtensions
{
    public static IApplicationBuilder UseFoundationTelemetry(this IApplicationBuilder app)
        => app.UseMiddleware<FoundationTelemetryMiddleware>();
}
