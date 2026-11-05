using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Web.Middlewares;
using OpenIddict.Abstractions;
using Xunit;

public sealed class TenantResolutionMiddlewareTests
{
    private static ClaimsPrincipal User(Guid userId, string context, Guid? tenantId = null, string? role = null)
    {
        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, userId.ToString()),
            new("account_type", context)
        };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (role is not null)
            claims.Add(new Claim(OpenIddictConstants.Claims.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static async Task<(DefaultHttpContext Http, TenantContext Context, bool NextCalled)> Execute(
        ClaimsPrincipal principal,
        FakeTenantAccessResolver? resolver = null,
        string? requestTenantHeader = null)
    {
        var context = new TenantContext();
        var http = new DefaultHttpContext { User = principal };
        if (requestTenantHeader is not null)
            http.Request.Headers["X-Tenant-Id"] = requestTenantHeader;

        var called = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http, context, resolver ?? new FakeTenantAccessResolver());
        return (http, context, called);
    }

    [Fact]
    public async Task Anonymous_request_proceeds_without_tenant_context()
    {
        var result = await Execute(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(result.NextCalled);
        Assert.Null(result.Context.UserId);
    }

    [Fact]
    public async Task System_administrator_is_not_implicitly_a_tenant()
    {
        var userId = Guid.NewGuid();
        var result = await Execute(User(userId, "system", role: "SuperAdmin"));

        Assert.True(result.NextCalled);
        Assert.True(result.Context.IsSystemContext);
        Assert.True(result.Context.IsSuperAdmin);
        Assert.Equal(userId, result.Context.UserId);
        Assert.Null(result.Context.HomeTenantId);
    }

    [Fact]
    public async Task System_token_containing_tenant_id_is_rejected()
    {
        var result = await Execute(User(Guid.NewGuid(), "system", Guid.NewGuid()));

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Http.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_requires_authorized_home_tenant_scope()
    {
        var tenantId = Guid.NewGuid();
        var result = await Execute(User(Guid.NewGuid(), "tenant", tenantId));

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Http.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_with_read_access_populates_current_scope()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new FakeTenantAccessResolver
        {
            Readable = [tenantId],
            Writable = [tenantId]
        };

        var result = await Execute(User(Guid.NewGuid(), "tenant", tenantId), resolver);

        Assert.True(result.NextCalled);
        Assert.False(result.Context.IsSystemContext);
        Assert.Equal(tenantId, result.Context.ActiveTenantId);
        Assert.Contains(tenantId, result.Context.WritableTenantIds);
    }

    [Fact]
    public async Task Header_cannot_select_another_tenant()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new FakeTenantAccessResolver { Readable = [tenantId], Writable = [tenantId] };

        var result = await Execute(User(Guid.NewGuid(), "tenant", tenantId), resolver, Guid.NewGuid().ToString());

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Http.Response.StatusCode);
    }

    [Fact]
    public async Task Invalid_context_is_rejected()
    {
        var result = await Execute(User(Guid.NewGuid(), "unexpected"));

        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Http.Response.StatusCode);
    }

    private sealed class FakeTenantAccessResolver : ITenantAccessResolver
    {
        public Guid[] Readable { get; init; } = [];
        public Guid[] Writable { get; init; } = [];

        public Task<(Guid[] Readable, Guid[] Writable)> ResolveAsync(Guid userId, Guid homeTenantId, CancellationToken ct)
            => Task.FromResult((Readable, Writable));
    }
}
