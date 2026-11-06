using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Foundation.Bff.Tests;

/// <summary>
/// BFF-05.2: the first cookie-authenticated unsafe endpoint requires both a
/// valid antiforgery token and same-origin request metadata, and actually
/// revokes the ticket in the server-side ITicketStore on logout.
/// </summary>
public sealed partial class BffSessionHttpTests
{
    [Fact]
    public async Task Csrf_bootstrap_requires_a_valid_cookie_and_does_not_accept_bearer_only()
    {
        await using var h = await Harness.StartAsync();
        using var anonymous = await h.GetCsrfAsync(null, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("no-store", anonymous.Headers.CacheControl?.ToString());

        using var bearer = await h.GetCsrfAsync(null,
            "tenant-a.shinera.localhost", bearer: true);
        Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);

        var cookie = (await h.IssueSessionAsync("tenant-a.shinera.localhost")).Split(';')[0];
        using var valid = await h.GetCsrfAsync(cookie, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Contains("no-store", valid.Headers.CacheControl?.ToString());
        var (requestToken, antiforgeryCookie) = ReadCsrfCookies(valid);
        Assert.NotEmpty(requestToken);
        Assert.NotEmpty(antiforgeryCookie);
        Assert.DoesNotContain("access_token", requestToken, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("tenant-a.shinera.localhost")]
    [InlineData("localhost")]
    public async Task Valid_csrf_logout_revokes_current_server_ticket(string host)
    {
        await using var h = await Harness.StartAsync();
        var bffCookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);
        var (token, antiforgeryCookie) = ReadCsrfCookies(bootstrap);

        using var missing = await h.LogoutAsync(bffCookie, host,
            antiforgeryCookie: antiforgeryCookie);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("no-store", missing.Headers.CacheControl?.ToString());

        using var stillActive = await h.ReadSessionAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);

        using var logout = await h.LogoutAsync(bffCookie, host,
            token: token, antiforgeryCookie: antiforgeryCookie,
            origin: "https://" + host, fetchSite: "same-origin");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("no-store", logout.Headers.CacheControl?.ToString());

        using var revoked = await h.ReadSessionAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Csrf_token_for_one_authenticated_user_cannot_logout_a_different_user()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var firstCookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        var secondCookie = (await h.IssueSessionAsync(host)).Split(';')[0];

        using var firstCsrf = await h.GetCsrfAsync(firstCookie, host);
        Assert.Equal(HttpStatusCode.NoContent, firstCsrf.StatusCode);
        var (firstToken, firstAntiCookie) = ReadCsrfCookies(firstCsrf);

        // The antiforgery token must be bound to the validated BFF principal,
        // not to an anonymous default bearer scheme.
        using var rejected = await h.LogoutAsync(
            secondCookie, host, firstToken, firstAntiCookie,
            origin: "https://" + host, fetchSite: "same-origin");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var secondStillActive = await h.ReadSessionAsync(secondCookie, host);
        Assert.Equal(HttpStatusCode.OK, secondStillActive.StatusCode);
        using var firstStillActive = await h.ReadSessionAsync(firstCookie, host);
        Assert.Equal(HttpStatusCode.OK, firstStillActive.StatusCode);
    }

    [Fact]
    public async Task Forged_antiforgery_token_cannot_logout_a_session()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var bffCookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(bffCookie, host);
        var (_, antiCookie) = ReadCsrfCookies(bootstrap);

        using var logout = await h.LogoutAsync(bffCookie, host,
            token: "invalid-xsrf-request-token", antiforgeryCookie: antiCookie);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);

        using var session = await h.ReadSessionAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_cookie_and_csrf_replay_cannot_logout_another_host()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var bffCookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(bffCookie, host);
        var (token, antiCookie) = ReadCsrfCookies(bootstrap);

        using var wrongHost = await h.LogoutAsync(bffCookie,
            "tenant-b.shinera.localhost", token, antiCookie);
        Assert.Equal(HttpStatusCode.Forbidden, wrongHost.StatusCode);

        using var stillActive = await h.ReadSessionAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example", "same-origin")]
    [InlineData("https://tenant-b.shinera.localhost", "same-site")]
    [InlineData("https://tenant-a.shinera.localhost", "cross-site")]
    public async Task Wrong_origin_or_fetch_metadata_is_refused_before_logout(
        string origin, string fetchSite)
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var bffCookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(bffCookie, host);
        var (token, antiCookie) = ReadCsrfCookies(bootstrap);

        using var logout = await h.LogoutAsync(bffCookie, host,
            token, antiCookie, origin, fetchSite);
        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);

        using var stillActive = await h.ReadSessionAsync(bffCookie, host);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Fact]
    public async Task Anonymous_logout_is_401_and_disabled_bff_does_not_map_lifecycle_endpoints()
    {
        await using var active = await Harness.StartAsync();
        using var anonymous = await active.LogoutAsync(null, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await using var disabled = await Harness.StartAsync(enabled: false);
        using var csrf = await disabled.GetCsrfAsync(null, "tenant-a.shinera.localhost");
        using var logout = await disabled.LogoutAsync(null, "tenant-a.shinera.localhost");
        Assert.Equal(HttpStatusCode.NotFound, csrf.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logout.StatusCode);
    }

    private static (string Token, string AntiforgeryCookie) ReadCsrfCookies(
        HttpResponseMessage response)
    {
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var readableToken = Assert.Single(cookies,
            x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", readableToken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", readableToken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", readableToken, StringComparison.OrdinalIgnoreCase);

        var antiforgery = Assert.Single(cookies,
            x => x.StartsWith("__Host-Foundation.Antiforgery=", StringComparison.Ordinal));
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", antiforgery, StringComparison.OrdinalIgnoreCase);

        var token = Uri.UnescapeDataString(readableToken.Split(';')[0]["XSRF-TOKEN=".Length..]);
        return (token, antiforgery.Split(';')[0]);
    }

    private sealed partial class Harness
    {
        public async Task<HttpResponseMessage> GetCsrfAsync(
            string? bffCookie, string host, bool bearer = false)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, "https://" + host + "/bff/auth/csrf");
            if (!string.IsNullOrEmpty(bffCookie))
                request.Headers.TryAddWithoutValidation("Cookie", bffCookie);
            if (bearer)
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-bearer");
            return await _client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> LogoutAsync(
            string? bffCookie,
            string host,
            string? token = null,
            string? antiforgeryCookie = null,
            string? origin = null,
            string? fetchSite = null)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://" + host + "/bff/auth/logout");
            var cookies = new List<string>();
            if (!string.IsNullOrEmpty(bffCookie))
                cookies.Add(bffCookie);
            if (!string.IsNullOrEmpty(antiforgeryCookie))
                cookies.Add(antiforgeryCookie);
            if (cookies.Count != 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
            if (token is not null)
                request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);
            if (origin is not null)
                request.Headers.TryAddWithoutValidation("Origin", origin);
            if (fetchSite is not null)
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
            return await _client.SendAsync(request);
        }
    }
}
