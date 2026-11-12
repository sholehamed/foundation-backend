using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Modules.System.Observability;

/// <summary>
/// FR-18E-1: bounded, allowlisted projection from a private Tempo backend.
/// This class is NOT an authorization gate: the System host endpoint must
/// check live persisted System role and explicit Control Tower grant.
/// </summary>
public sealed class FoundationTempoTraceReader(
    HttpClient client, IConfiguration config, IHostEnvironment environment)
{
    private const int MaxTraceBytes = 1_048_576;
    private const int MaxSpans = 256;
    private static readonly Regex TraceIdPattern = new(
        "^[a-fA-F0-9]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpanIdPattern = new(
        "^[a-fA-F0-9]{16}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeLabelPattern = new(
        "^[a-zA-Z0-9_./{} -]{1,120}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedAttributes =
        new(StringComparer.Ordinal)
        {
            "http.route", "http.request.method", "http.response.status_code",
            "foundation.cqrs.kind", "foundation.cqrs.request_type",
            "foundation.db.client", "foundation.db.execution",
            "foundation.db.command_type", "foundation.db.command_source",
            "foundation.db.context_type", "foundation.db.parameter_count",
            "foundation.db.fingerprint", "foundation.db.fingerprint_scope",
            "foundation.db.fingerprint_state", "foundation.db.outcome",
            "foundation.db.transaction.kind", "foundation.db.transaction.action",
            "foundation.db.transaction.outcome", "foundation.error.type"
        };

    public sealed record Span(
        string SpanId, string? ParentSpanId, string Name, string Category,
        DateTimeOffset StartedAtUtc, double DurationMs, bool Error,
        IReadOnlyDictionary<string, string> Attributes);

    public sealed record Trace(
        string TraceId, string State, string Service, DateTimeOffset StartedAtUtc,
        double DurationMs, IReadOnlyList<Span> Spans, bool Truncated);

    public enum ReadState { Captured, Missing, Unavailable, Invalid }

    public sealed record ReadResult(ReadState State, Trace? Data)
    {
        public static ReadResult From(ReadState state) => new(state, null);
    }

    public async Task<ReadResult> ReadAsync(string traceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(traceId) || !TraceIdPattern.IsMatch(traceId))
            return ReadResult.From(ReadState.Invalid);

        if (!config.GetValue<bool>("Observability:Read:Enabled"))
            return ReadResult.From(ReadState.Unavailable);

        var baseAddress = config["Observability:Read:TempoEndpoint"];
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var endpoint) ||
            !string.IsNullOrWhiteSpace(endpoint.UserInfo) ||
            endpoint.AbsolutePath != "/" ||
            !string.IsNullOrWhiteSpace(endpoint.Query) ||
            !string.IsNullOrWhiteSpace(endpoint.Fragment) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             !(environment.IsDevelopment() &&
               endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
            return ReadResult.From(ReadState.Unavailable);

        // Constant server-controlled path and endpoint; no user-provided URL,
        // URL path, search syntax, filters or redirects.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(endpoint, "api/v2/traces/" + traceId.ToLowerInvariant()));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return ReadResult.From(ReadState.Unavailable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ReadResult.From(ReadState.Missing);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength > MaxTraceBytes)
                return ReadResult.From(ReadState.Unavailable);

            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await source.ReadAsync(chunk, cancellationToken)) != 0)
                {
                    if (buffer.Length + count > MaxTraceBytes)
                        return ReadResult.From(ReadState.Unavailable);
                    buffer.Write(chunk, 0, count);
                }
                buffer.Position = 0;
                using var json = await JsonDocument.ParseAsync(buffer,
                    new JsonDocumentOptions { MaxDepth = 24 }, cancellationToken);
                return Parse(traceId.ToLowerInvariant(), json.RootElement);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException
                                       or InvalidOperationException or IOException)
            {
                return ReadResult.From(ReadState.Unavailable);
            }
        }
    }

    internal static ReadResult Parse(string requestedTraceId, JsonElement root)
    {
        if (!TryProperty(root, "trace", out var trace)) trace = root;
        if (!TryProperty(trace, "resourceSpans", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
            return ReadResult.From(ReadState.Unavailable);

        var results = new List<Span>();
        var services = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;
        foreach (var resource in resources.EnumerateArray())
        {
            if (TryProperty(resource, "resource", out var resourceObject) &&
                TryProperty(resourceObject, "attributes", out var serviceAttributes) &&
                serviceAttributes.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in serviceAttributes.EnumerateArray())
                    if (ReadString(entry, "key") == "service.name" &&
                        TryProperty(entry, "value", out var value) &&
                        ReadString(value, "stringValue") is string label &&
                        SafeLabelPattern.IsMatch(label))
                        services.Add(label);
            }

            if (!TryProperty(resource, "scopeSpans", out var scopes) ||
                scopes.ValueKind != JsonValueKind.Array) continue;

            foreach (var scope in scopes.EnumerateArray())
            {
                if (!TryProperty(scope, "spans", out var spans) ||
                    spans.ValueKind != JsonValueKind.Array) continue;
                foreach (var span in spans.EnumerateArray())
                {
                    if (results.Count == MaxSpans)
                    {
                        truncated = true;
                        break;
                    }

                    var id = NormalizeId(ReadString(span, "spanId"), 8);
                    var parent = NormalizeId(ReadString(span, "parentSpanId"), 8);
                    var recordTrace = NormalizeId(ReadString(span, "traceId"), 16);
                    if (id is null || recordTrace != requestedTraceId)
                        return ReadResult.From(ReadState.Unavailable);
                    if (!long.TryParse(ReadString(span, "startTimeUnixNano"),
                            NumberStyles.None, CultureInfo.InvariantCulture, out var startNs) ||
                        !long.TryParse(ReadString(span, "endTimeUnixNano"),
                            NumberStyles.None, CultureInfo.InvariantCulture, out var endNs) ||
                        endNs < startNs || startNs < 0)
                        return ReadResult.From(ReadState.Unavailable);

                    var attributes = SafeAttributes(span);
                    // Do not reflect potentially attacker-controlled span names:
                    // project only documented code-owned span categories.
                    var rawName = ReadString(span, "name") ?? "";
                    var name = SafeName(rawName);
                    var category = name.StartsWith("db.", StringComparison.Ordinal) ? "sql" :
                        name.StartsWith("cqrs.", StringComparison.Ordinal) ? "handler" :
                        name == "foundation.http.pipeline" ? "pipeline" : "http";
                    var state = TryProperty(span, "status", out var status)
                        ? ReadString(status, "code") : null;
                    var error = state is "2" or "STATUS_CODE_ERROR";
                    results.Add(new Span(id, parent, name, category,
                        DateTimeOffset.FromUnixTimeMilliseconds(startNs / 1_000_000),
                        Math.Round((endNs - startNs) / 1_000_000.0, 3),
                        error, attributes));
                }
                if (truncated) break;
            }
            if (truncated) break;
        }

        if (results.Count == 0) return ReadResult.From(ReadState.Missing);
        var min = results.Min(x => x.StartedAtUtc);
        var max = results.Max(x => x.StartedAtUtc.AddMilliseconds(x.DurationMs));
        var service = services.Count == 1 ? services.Single() : "unattributed";
        return new ReadResult(ReadState.Captured,
            new Trace(requestedTraceId, truncated ? "PartiallyCaptured" : "Captured",
                service, min, Math.Max(0, (max - min).TotalMilliseconds),
                results.OrderBy(x => x.StartedAtUtc).ToArray(), truncated));
    }

    private static IReadOnlyDictionary<string, string> SafeAttributes(JsonElement span)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryProperty(span, "attributes", out var list) ||
            list.ValueKind != JsonValueKind.Array) return attrs;
        foreach (var item in list.EnumerateArray())
        {
            var key = ReadString(item, "key");
            if (key is null || !AllowedAttributes.Contains(key) ||
                !TryProperty(item, "value", out var value)) continue;
            var raw = ReadString(value, "stringValue") ??
                      ReadString(value, "intValue") ??
                      ReadString(value, "boolValue");
            if (raw is null || raw.Length > 120) continue;
            if (key == "foundation.db.fingerprint" &&
                (raw.Length != 64 || !raw.All(Uri.IsHexDigit))) continue;
            if (key == "http.route" && !SafeLabelPattern.IsMatch(raw)) continue;
            if (key is not ("http.route" or "foundation.db.fingerprint") &&
                !SafeLabelPattern.IsMatch(raw)) continue;
            attrs[key] = raw;
        }
        return attrs;
    }

    private static string SafeName(string value)
    {
        if (value is "foundation.http.pipeline" or "cqrs.command" or "cqrs.query" or
            "cqrs.notification" or "db.ef.command" or "db.sqlclient.command" or
            "db.sqlclient.transaction" or "db.sqlclient.transaction.commit" or
            "db.sqlclient.transaction.rollback") return value;
        // HTTP server Activities may have a code-owned templated route name;
        // never mirror a raw path or arbitrary producer-controlled text.
        return "http.server";
    }

    private static bool TryProperty(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
            return item.TryGetProperty(name, out value);
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!TryProperty(item, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? NormalizeId(string? value, int bytes)
    {
        if (value is null) return null;
        if (value.Length == bytes * 2 && value.All(Uri.IsHexDigit))
            return value.ToLowerInvariant();
        try
        {
            var binary = Convert.FromBase64String(value);
            return binary.Length == bytes ? Convert.ToHexStringLower(binary) : null;
        }
        catch (FormatException) { return null; }
    }
}
