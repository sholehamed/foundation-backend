using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Modules.System.Observability;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class ControlTowerTempoReaderTests
{
    private sealed class FakeHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "fr18e-test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer(request));
        }
    }

    private static FoundationTempoTraceReader Create(
        FakeHandler handler, bool enabled = true, string? endpoint = "http://127.0.0.1:3200/",
        IHostEnvironment? environment = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Observability:Read:Enabled"] = enabled.ToString(),
                ["Observability:Read:TempoEndpoint"] = endpoint
            }).Build();
        return new(new HttpClient(handler), config, environment ?? new FakeHost());
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task Disabled_invalid_id_or_bad_endpoint_makes_no_outgoing_request()
    {
        var h = new FakeHandler(_ => Json("{}"));
        Assert.Equal(FoundationTempoTraceReader.ReadState.Invalid,
            (await Create(h).ReadAsync("../etc/passwd", default)).State);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(h, enabled: false).ReadAsync(new string('a', 32), default)).State);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(h, endpoint: "https://user:secret@tempo.test/?q=secret")
                .ReadAsync(new string('a', 32), default)).State);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(h, endpoint: "http://192.168.1.5:3200/")
                .ReadAsync(new string('a', 32), default)).State);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(h, environment: new FakeHost { EnvironmentName = Environments.Production })
                .ReadAsync(new string('a', 32), default)).State);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Real_tempo_shape_returns_only_allowlisted_safe_attributes_and_never_raw_payload()
    {
        var id = new string('a', 32);
        var original = new {
            trace = new {
                resourceSpans = new[] { new {
                    resource = new { attributes = new[] { new {
                        key = "service.name", value = new { stringValue = "foundation-web" }
                    }}},
                    scopeSpans = new[] { new {
                        spans = new[] { new {
                            traceId = id, spanId = "1111111111111111",
                            parentSpanId = "2222222222222222",
                            name = "db.ef.command",
                            startTimeUnixNano = "1810010000000000000",
                            endTimeUnixNano = "1810010000002500000",
                            attributes = new object[] {
                                new { key = "db.statement", value = new { stringValue = "SELECT customer_password" }},
                                new { key = "http.request.header.authorization", value = new { stringValue = "Bearer top-secret" }},
                                new { key = "foundation.db.client", value = new { stringValue = "ef-core" }},
                                new { key = "foundation.db.fingerprint", value = new { stringValue = new string('f', 64) }},
                                new { key = "http.route", value = new { stringValue = "/api/system/dashboard/{id}" }},
                                new { key = "foundation.error.type", value = new { stringValue = "CREDENTIAL_leak!?!" }}
                            },
                            status = new { code = 1 }
                        }}
                    }}
                }}
            }
        };
        var receivedUri = "";
        var handler = new FakeHandler(r =>
        {
            receivedUri = r.RequestUri?.ToString() ?? "";
            return Json(System.Text.Json.JsonSerializer.Serialize(original));
        });
        var result = await Create(handler).ReadAsync(id, default);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Captured, result.State);
        Assert.NotNull(result.Data);
        Assert.Equal("foundation-web", result.Data.Service);
        Assert.Single(result.Data.Spans);
        var span = result.Data.Spans[0];
        Assert.Equal(2.5, span.DurationMs);
        Assert.Equal("sql", span.Category);
        Assert.Equal("ef-core", span.Attributes["foundation.db.client"]);
        Assert.False(span.Attributes.ContainsKey("db.statement"));
        Assert.False(span.Attributes.ContainsKey("http.request.header.authorization"));
        Assert.False(span.Attributes.ContainsKey("foundation.error.type"));
        Assert.Equal(64, span.Attributes["foundation.db.fingerprint"].Length);
        Assert.Equal("http://127.0.0.1:3200/api/v2/traces/" + id, receivedUri);
        Assert.DoesNotContain("SELECT", System.Text.Json.JsonSerializer.Serialize(result.Data));
        Assert.DoesNotContain("Bearer", System.Text.Json.JsonSerializer.Serialize(result.Data));
    }

    [Fact]
    public async Task Missing_and_failed_upstream_are_distinct_and_fail_closed()
    {
        var id = new string('b', 32);
        var notFound = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(FoundationTempoTraceReader.ReadState.Missing,
            (await Create(notFound).ReadAsync(id, default)).State);

        var redirected = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://untrusted.example/collect") }
        });
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(redirected).ReadAsync(id, default)).State);
        Assert.Equal(1, redirected.Calls);
    }

    [Fact]
    public async Task Wrong_trace_id_and_oversized_body_are_never_returned()
    {
        var id = new string('c', 32);
        var foreign = new string('d', 32);
        var json = "{\"trace\":{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"traceId\":\""
            + foreign + "\",\"spanId\":\"1111111111111111\",\"name\":\"db.ef.command\","
            + "\"startTimeUnixNano\":\"1810010000000000000\","
            + "\"endTimeUnixNano\":\"1810010000002500000\"}]}]}]}}";
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(new FakeHandler(_ => Json(json))).ReadAsync(id, default)).State);
        Assert.Equal(FoundationTempoTraceReader.ReadState.Unavailable,
            (await Create(new FakeHandler(_ => Json(new string('x', 1_048_580))))
                .ReadAsync(id, default)).State);
    }
}
