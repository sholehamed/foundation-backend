using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

/// <summary>
/// Deterministic interleavings shared by multiple ITicketStore instances.
/// These cover an IDistributedCache backed by ONE shared, consistent provider;
/// not Redis/SQL failover, eviction or multi-region consistency guarantees.
/// </summary>
public sealed class DistributedBffTicketRevocationTests
{
    [Fact]
    public async Task In_flight_renewal_cannot_resurrect_a_logged_out_session()
    {
        using var provider = CreateProvider();
        var transport = new PausingCache(provider.GetRequiredService<IDistributedCache>());
        var protection = provider.GetRequiredService<IDataProtectionProvider>();
        var renewer = new DistributedBffTicketStore(transport, protection);
        var logout = new DistributedBffTicketStore(transport, protection);
        var original = NewTicket("alice");
        var key = await renewer.StoreAsync(original);
        var renewal = RenewedTicket("alice", original);

        transport.PauseNextTicketWrite();
        var inFlightRenewal = renewer.RenewAsync(key, renewal);
        await transport.WaitForTicketWriteAsync();

        await logout.RemoveAsync(key);
        Assert.Null(await logout.RetrieveAsync(key));

        transport.ReleaseTicketWrite();
        await inFlightRenewal;

        // The late SetAsync is deleted after renewal notices the tombstone,
        // while every retrieval must also reject before and after reading.
        Assert.Null(await renewer.RetrieveAsync(key));
        Assert.Null(await logout.RetrieveAsync(key));
        Assert.Null(await transport.GetAsync(key));
    }

    [Fact]
    public async Task In_flight_cached_read_must_recheck_revocation_after_logout()
    {
        using var provider = CreateProvider();
        var transport = new PausingCache(provider.GetRequiredService<IDistributedCache>());
        var protection = provider.GetRequiredService<IDataProtectionProvider>();
        var reader = new DistributedBffTicketStore(transport, protection);
        var logout = new DistributedBffTicketStore(transport, protection);
        var key = await reader.StoreAsync(NewTicket("alice"));

        // Return a stale snapshot of the ticket bytes, but only after the
        // logout has fully persisted its revocation marker and removed key.
        transport.PauseNextTicketRead();
        var inFlightRead = reader.RetrieveAsync(key);
        await transport.WaitForTicketReadAsync();
        await logout.RemoveAsync(key);
        transport.ReleaseTicketRead();

        Assert.Null(await inFlightRead);
        Assert.Null(await reader.RetrieveAsync(key));
    }

    [Fact]
    public async Task Previously_revoked_key_rejects_future_renewals_even_with_old_ticket()
    {
        using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var protection = provider.GetRequiredService<IDataProtectionProvider>();
        var first = new DistributedBffTicketStore(cache, protection);
        var second = new DistributedBffTicketStore(cache, protection);
        var original = NewTicket("alice");
        var key = await first.StoreAsync(original);
        var oldBytes = await cache.GetAsync(key);
        Assert.NotNull(oldBytes);
        var renewed = RenewedTicket("alice", original);

        await second.RemoveAsync(key);
        await first.RenewAsync(key, renewed);
        Assert.Null(await first.RetrieveAsync(key));

        // Even if storage accidentally replays pre-revocation ticket bytes,
        // a still-retained independent tombstone must fail closed.
        await cache.SetAsync(key, oldBytes!, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });
        Assert.Null(await first.RetrieveAsync(key));
    }

    [Fact]
    public async Task Renewal_is_bounded_by_initial_absolute_expiry_not_new_issued_time()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();
        var issued = DateTimeOffset.UtcNow.AddMinutes(-20);
        var initial = NewTicket("alice");
        initial.Properties.IssuedUtc = issued;
        initial.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(50);
        var key = await store.StoreAsync(initial);
        var saved = await store.RetrieveAsync(key);
        Assert.NotNull(saved);
        Assert.True(saved.Properties.ExpiresUtc <= issued.AddHours(24));

        var renewed = RenewedTicket("alice", initial);
        renewed.Properties.IssuedUtc = DateTimeOffset.UtcNow;
        renewed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(50);
        await store.RenewAsync(key, renewed);
        var after = await store.RetrieveAsync(key);
        Assert.NotNull(after);
        Assert.True(after.Properties.ExpiresUtc <= issued.AddHours(24));
        Assert.Equal("alice", after.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Expired_original_lifetime_and_unbound_renewal_fail_closed()
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<ITicketStore>();
        var expired = NewTicket("old");
        expired.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-25);
        expired.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(expired));

        var initial = NewTicket("alice");
        var key = await store.StoreAsync(initial);
        var freshWithoutBaseline = NewTicket("bob");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RenewAsync(key, freshWithoutBaseline));
        Assert.Equal("alice",
            (await store.RetrieveAsync(key))?.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Logout_creates_marker_longer_than_any_allowed_ticket_lifetime()
    {
        using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var store = provider.GetRequiredService<ITicketStore>();
        var key = await store.StoreAsync(NewTicket("alice"));
        await store.RemoveAsync(key);
        var suffix = key[BffDefaults.TicketCachePrefix.Length..];
        Assert.NotNull(await cache.GetAsync("foundation:bff:revoked:v1:" + suffix));
        Assert.Null(await cache.GetAsync(key));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddBffSessionInfrastructure();
        return services.BuildServiceProvider();
    }

    private static AuthenticationTicket NewTicket(string userId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)], BffDefaults.CookieScheme));
        var props = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(45)
        };
        return new AuthenticationTicket(principal, props, BffDefaults.CookieScheme);
    }

    private static AuthenticationTicket RenewedTicket(
        string userId, AuthenticationTicket source)
    {
        var ticket = NewTicket(userId);
        foreach (var prop in source.Properties.Items)
            ticket.Properties.Items[prop.Key] = prop.Value;
        return ticket;
    }

    private sealed class PausingCache(IDistributedCache inner) : IDistributedCache
    {
        private int _pauseWrite;
        private int _pauseRead;
        private readonly TaskCompletionSource _writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resumeWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resumeRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void PauseNextTicketWrite() => Interlocked.Exchange(ref _pauseWrite, 1);
        public void PauseNextTicketRead() => Interlocked.Exchange(ref _pauseRead, 1);
        public Task WaitForTicketWriteAsync() => _writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public Task WaitForTicketReadAsync() => _readStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void ReleaseTicketWrite() => _resumeWrite.TrySetResult();
        public void ReleaseTicketRead() => _resumeRead.TrySetResult();

        public byte[]? Get(string key) => inner.Get(key);

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            var result = await inner.GetAsync(key, token);
            if (key.StartsWith(BffDefaults.TicketCachePrefix, StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _pauseRead, 0, 1) == 1)
            {
                _readStarted.TrySetResult();
                await _resumeRead.Task.WaitAsync(token);
            }
            return result;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => inner.Set(key, value, options);

        public async Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            if (key.StartsWith(BffDefaults.TicketCachePrefix, StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _pauseWrite, 0, 1) == 1)
            {
                _writeStarted.TrySetResult();
                await _resumeWrite.Task.WaitAsync(token);
            }
            await inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default)
            => inner.RefreshAsync(key, token);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
            => inner.RemoveAsync(key, token);
    }
}
