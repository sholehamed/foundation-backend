using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Observability;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// FR-18B: real in-process HTTP pipeline; no Identity SQL DB, Collector, body
/// capture or telemetry-specific production test endpoint.
/// </summary>
public sealed class FoundationObservabilityHostTests
{
    [Fact]
    public async Task W3c_trace_and_request_outcome_survive_success_forbidden_and_failure_without_a_collector()
    {
        var captured = new List<(string? TraceId, string? Operation)>();
        await using var app = await StartAsync(captured);
        var client = app.GetTestClient();

        const string knownTrace = "4bf92f3577b34da6a3ce929d0e0e4736";
        using var successRequest = new HttpRequestMessage(HttpMethod.Post,
            "/fixture/orders?password=no-log-this-token");
        successRequest.Headers.Add("traceparent",
            "00-" + knownTrace + "-00f067aa0ba902b7-01");
        successRequest.Headers.Add("baggage", "tenant.id=forged,role=systemadmin");
        successRequest.Headers.Add("Authorization", "Bearer secret-token-not-for-logs");
        successRequest.Content = new StringContent(
            "{\"password\":\"secret-body-must-not-be-read\"}");

        using var success = await client.SendAsync(successRequest);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(knownTrace,
            Assert.Single(success.Headers.GetValues("X-Trace-Id")));
        Assert.Contains(captured, c => c.TraceId == knownTrace &&
            c.Operation == "foundation.http.pipeline");

        using var forbidden = await client.GetAsync("/fixture/denied");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Matches("^[a-f0-9]{32}$",
            Assert.Single(forbidden.Headers.GetValues("X-Trace-Id")));

        using var failed = await client.GetAsync("/fixture/failed");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Matches("^[a-f0-9]{32}$",
            Assert.Single(failed.Headers.GetValues("X-Trace-Id")));

        using var auth = await client.GetAsync("/bff/auth/login?code=secret");
        Assert.Equal(HttpStatusCode.NotFound, auth.StatusCode);
        Assert.False(auth.Headers.Contains("X-Trace-Id"));
    }

    [Fact]
    public void Privacy_processor_scrubs_unsafe_tags_and_never_uses_raw_request_path_in_name()
    {
        using var source = new ActivitySource("fr18b.privacy.fixture");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("GET /tenant/customer/personal-value");
        Assert.NotNull(activity);
        activity.SetTag("url.full", "https://service.test/private?password=private-secret");
        activity.SetTag("http.request.header.authorization", "Bearer private-token");
        activity.SetTag("db.statement", "SELECT * FROM secret");
        activity.SetTag("exception.message", "sensitive customer information");
        activity.SetTag("http.route", "/fixture/{id}");
        new FoundationTelemetryPrivacy().OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.full"));
        Assert.Null(activity.GetTagItem("http.request.header.authorization"));
        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Null(activity.GetTagItem("exception.message"));
        Assert.False(FoundationTelemetryPrivacy.CanTracePath("/api/system/Auth/login"));
        Assert.False(FoundationTelemetryPrivacy.CanTracePath("/api/connect/authorize"));
        Assert.False(FoundationTelemetryPrivacy.CanTracePath("/bff/auth/logout"));
        Assert.True(FoundationTelemetryPrivacy.CanTracePath("/api/tenant/workspace/members"));
        Assert.Equal("OTHER", FoundationTelemetryPrivacy.NormalizeMethod("FANCY_METHOD"));
    }

    [Fact]
    public void Export_defaults_off_and_rejects_unsafe_collector_config_without_activating_network()
    {
        var development = new HostEnvironment { EnvironmentName = Environments.Development };
        var production = new HostEnvironment { EnvironmentName = Environments.Production };

        IConfiguration Config(params (string Key, string? Value)[] values)
            => new ConfigurationBuilder().AddInMemoryCollection(
                values.ToDictionary(x => x.Key, x => x.Value)).Build();

        var off = FoundationObservabilitySettings.Read(Config(), development);
        Assert.False(off.ExportEnabled);
        Assert.Null(off.CollectorEndpoint);
        Assert.Equal(0.1, off.TraceSampleRatio);

        Assert.Throws<InvalidOperationException>(() =>
            FoundationObservabilitySettings.Read(Config(
                ("Observability:Otlp:Enabled", "true")), production));
        Assert.Throws<InvalidOperationException>(() =>
            FoundationObservabilitySettings.Read(Config(
                ("Observability:Otlp:Enabled", "true"),
                ("Observability:Otlp:Endpoint", "http://collector.internal:4317")), production));
        Assert.Throws<InvalidOperationException>(() =>
            FoundationObservabilitySettings.Read(Config(
                ("Observability:Otlp:Enabled", "true"),
                ("Observability:Otlp:Endpoint", "https://collector:4317/?token=secret")), production));
        Assert.Throws<InvalidOperationException>(() =>
            FoundationObservabilitySettings.Read(Config(
                ("Observability:TraceSampleRatio", "1.2")), development));
        var allowed = FoundationObservabilitySettings.Read(Config(
            ("Observability:Otlp:Enabled", "true"),
            ("Observability:Otlp:Endpoint", "https://collector.internal:4317")), production);
        Assert.True(allowed.ExportEnabled);
        Assert.Equal("https", allowed.CollectorEndpoint?.Scheme);
    }

    [Fact]
    public async Task Inaccessible_local_collector_does_not_delay_or_block_a_business_response()
    {
        await using var app = await StartAsync(
            [],
            new Dictionary<string, string?>
            {
                ["Observability:Otlp:Enabled"] = "true",
                ["Observability:Otlp:Endpoint"] = "http://127.0.0.1:1"
            });
        using var response = await app.GetTestClient().GetAsync("/fixture/denied");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }

    private static async Task<WebApplication> StartAsync(
        List<(string? TraceId, string? Operation)> captured,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?>
        {
            ["Observability:Otlp:Enabled"] = "false",
            ["Observability:TraceSampleRatio"] = "1",
        };
        if (settings is not null)
            foreach (var pair in settings) values[pair.Key] = pair.Value;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddFoundationObservability(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseFoundationTelemetry();
        app.MapPost("/fixture/orders", (HttpContext http) =>
        {
            captured.Add((Activity.Current?.TraceId.ToString(),
                Activity.Current?.DisplayName));
            Assert.Empty(Activity.Current?.Baggage ?? []);
            return Results.Ok(new { saved = true });
        });
        app.MapGet("/fixture/denied", () => Results.StatusCode(403));
        app.MapGet("/fixture/failed", () => Results.StatusCode(500));
        await app.StartAsync();
        return app;
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "observability-test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
