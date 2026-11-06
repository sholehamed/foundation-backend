using System.Net;
using Microsoft.AspNetCore.Http;
using Modules.System.Bff.Security;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed partial class BffSessionHttpTests
{
    [Fact]
    public async Task Logout_only_removes_the_selected_ticket_and_replay_is_unauthorized()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var first = (await h.IssueSessionAsync(host)).Split(';')[0];
        var second = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(first, host);
        var (token, antiCookie) = ReadCsrfCookies(bootstrap);

        // Non-browser clients may omit metadata; antiforgery is still required.
        using var logout = await h.LogoutAsync(first, host, token, antiCookie);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var replay = await h.LogoutAsync(first, host, token, antiCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var otherSession = await h.ReadSessionAsync(second, host);
        Assert.Equal(HttpStatusCode.OK, otherSession.StatusCode);
    }

    [Fact]
    public async Task Request_token_without_its_antiforgery_cookie_does_not_revoke_session()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var cookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(cookie, host);
        var (token, _) = ReadCsrfCookies(bootstrap);
        using var logout = await h.LogoutAsync(cookie, host, token);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        using var session = await h.ReadSessionAsync(cookie, host);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Get_logout_is_not_allowed_and_cross_host_csrf_bootstrap_is_refused()
    {
        await using var h = await Harness.StartAsync();
        const string host = "tenant-a.shinera.localhost";
        var cookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var getLogout = await h.GetLogoutAsync(cookie, host);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getLogout.StatusCode);
        using var csrf = await h.GetCsrfAsync(cookie, "localhost");
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        Assert.False(csrf.Headers.Contains("Set-Cookie"));
        using var session = await h.ReadSessionAsync(cookie, host);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    private sealed partial class Harness
    {
        public async Task<HttpResponseMessage> GetLogoutAsync(string cookie, string host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + host + "/bff/auth/logout");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            return await _client.SendAsync(request);
        }
    }
}

public sealed class BffUnsafeRequestGuardTests
{
    [Theory]
    [InlineData("http", null, null, null, false)]
    [InlineData("https", null, null, null, true)]
    [InlineData("https", null, "https://tenant.example/app", null, true)]
    [InlineData("https", null, "https://other.example/app", null, false)]
    [InlineData("https", null, "not-a-uri", null, false)]
    [InlineData("https", "null", null, null, false)]
    [InlineData("https", "https://tenant.example.evil", null, null, false)]
    [InlineData("https", "https://tenant.example:8443", null, null, false)]
    [InlineData("https", "https://tenant.example", null, "same-site", false)]
    [InlineData("https", "https://tenant.example", null, "none", true)]
    [InlineData("https", "https://other.example", "https://tenant.example/app", null, false)]
    public void Unsafe_requests_enforce_transport_and_origin_boundaries(
        string scheme, string? origin, string? referer, string? fetchSite, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = scheme;
        request.Host = new HostString("tenant.example");
        if (origin is not null) request.Headers["Origin"] = origin;
        if (referer is not null) request.Headers["Referer"] = referer;
        if (fetchSite is not null) request.Headers["Sec-Fetch-Site"] = fetchSite;
        Assert.Equal(expected, BffUnsafeRequestGuard.IsSameOrigin(request));
    }
}
