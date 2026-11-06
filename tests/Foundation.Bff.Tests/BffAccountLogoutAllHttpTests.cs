using System.Net;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed partial class BffSessionHttpTests
{
    [Fact]
    public async Task Logout_all_requires_bff_cookie_and_supported_atomic_revoker()
    {
        await using var h = await Harness.StartAsync();
        const string host = "localhost";

        using var anon = await h.LogoutAllAsync(null, host);
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var cookie = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(cookie, host);
        var (token, antiCookie) = ReadCsrfCookies(bootstrap);

        using var noProvider = await h.LogoutAllAsync(cookie, host,
            token, antiCookie, origin: "https://" + host, fetchSite: "same-origin");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noProvider.StatusCode);
        using var stillLoggedIn = await h.ReadSessionAsync(cookie, host);
        Assert.Equal(HttpStatusCode.OK, stillLoggedIn.StatusCode);
    }

    [Fact]
    public async Task Logout_all_validates_origin_csrf_and_rejects_cross_user_token()
    {
        var revoker = new RecordingRevoker();
        await using var h = await Harness.StartAsync(accountRevoker: revoker);
        const string host = "tenant-a.shinera.localhost";
        var first = (await h.IssueSessionAsync(host)).Split(';')[0];
        var second = (await h.IssueSessionAsync(host)).Split(';')[0];
        using var bootstrap = await h.GetCsrfAsync(first, host);
        var (token, antiCookie) = ReadCsrfCookies(bootstrap);

        using var wrongOrigin = await h.LogoutAllAsync(first, host,
            token, antiCookie, "https://tenant-b.shinera.localhost", "same-site");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        using var crossUser = await h.LogoutAllAsync(second, host,
            token, antiCookie, "https://" + host, "same-origin");
        Assert.Equal(HttpStatusCode.BadRequest, crossUser.StatusCode);
        using var withoutToken = await h.LogoutAllAsync(first, host,
            antiforgeryCookie: antiCookie, origin: "https://" + host,
            fetchSite: "same-origin");
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Empty(revoker.Subjects);

        // A malicious client body cannot select a different account: the
        // handler only receives the authenticated principal's sub claim.
        using var valid = await h.LogoutAllAsync(first, host,
            token, antiCookie, "https://" + host, "same-origin",
            body: "{\"subject\":\"00000000-0000-0000-0000-000000000001\"}");
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        var subject = Assert.Single(revoker.Subjects);
        Assert.NotEqual(Guid.Empty, subject);
        Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), subject);

        using var current = await h.ReadSessionAsync(first, host);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        // This is a recording fake, not SQL. SQL provider separately tests
        // that *other* sessions of the same subject are also revoked.
    }

    private sealed class RecordingRevoker : IBffAccountSessionRevoker
    {
        public List<Guid> Subjects { get; } = [];

        public Task<int> RevokeAllAsync(Guid subjectId, CancellationToken token = default)
        {
            Subjects.Add(subjectId);
            return Task.FromResult(1);
        }
    }

    private sealed partial class Harness
    {
        public async Task<HttpResponseMessage> LogoutAllAsync(
            string? bffCookie,
            string host,
            string? token = null,
            string? antiforgeryCookie = null,
            string? origin = null,
            string? fetchSite = null,
            string? body = null)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://" + host + "/bff/auth/logout-all");
            var cookies = new List<string>();
            if (bffCookie is not null)
                cookies.Add(bffCookie);
            if (antiforgeryCookie is not null)
                cookies.Add(antiforgeryCookie);
            if (cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
            if (token is not null)
                request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);
            if (origin is not null)
                request.Headers.TryAddWithoutValidation("Origin", origin);
            if (fetchSite is not null)
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
            if (body is not null)
                request.Content = new StringContent(body);
            return await _client.SendAsync(request);
        }
    }
}
