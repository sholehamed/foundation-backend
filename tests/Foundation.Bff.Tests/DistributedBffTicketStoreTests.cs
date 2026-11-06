using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed class DistributedBffTicketStoreTests
{
    [Fact]
    public async Task Store_roundtrip_and_remove_return_expected_session()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();
        var ticket = NewTicket("alice");

        var key = await store.StoreAsync(ticket);
        var restored = await store.RetrieveAsync(key);

        Assert.StartsWith(BffDefaults.TicketCachePrefix, key);
        Assert.NotNull(restored);
        Assert.Equal("alice", restored.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        await store.RemoveAsync(key);
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Multiple_sessions_do_not_share_lookup_keys()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();

        var first = await store.StoreAsync(NewTicket("alice"));
        var second = await store.StoreAsync(NewTicket("alice"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Renew_changes_ticket_without_changing_session_key()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();
        var original = NewTicket("alice");
        var key = await store.StoreAsync(original);
        var renewed = NewTicket("bob");
        // CookieAuthenticationHandler keeps the protected original session
        // absolute-expiry property during sliding renewal.
        foreach (var item in original.Properties.Items)
            renewed.Properties.Items[item.Key] = item.Value;

        await store.RenewAsync(key, renewed);

        Assert.Equal("bob",
            (await store.RetrieveAsync(key))?.Principal
                .FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Expired_tickets_are_rejected_when_stored()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreAsync(NewTicket("alice", DateTimeOffset.UtcNow.AddMinutes(-1))));
    }

    [Fact]
    public async Task Corrupt_cached_ticket_fails_closed()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var key = await store.StoreAsync(NewTicket("alice"));

        await cache.SetAsync(key, new byte[] { 1, 2, 3 },
            new DistributedCacheEntryOptions {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Invalid_keys_are_not_treated_as_sessions()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();

        Assert.Null(await store.RetrieveAsync("../other-key"));
        await store.RemoveAsync("../other-key");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddBffSessionInfrastructure();
        return services.BuildServiceProvider();
    }

    private static AuthenticationTicket NewTicket(
        string userId, DateTimeOffset? expires = null)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            BffDefaults.CookieScheme);
        var props = new AuthenticationProperties {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = expires ?? DateTimeOffset.UtcNow.AddMinutes(10)
        };
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity), props, BffDefaults.CookieScheme);
    }
}
