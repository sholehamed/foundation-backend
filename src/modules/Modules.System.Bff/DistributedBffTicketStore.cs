using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;

namespace Modules.System.Bff;

/// <summary>
/// Server-side AuthenticationTicket persistence. Browser cookies carry only
/// protected opaque lookup keys; OAuth tokens remain on the server.
///
/// A durable revocation marker guards against renewal/logout interleavings,
/// including renewal on another instance using the same strongly consistent
/// shared cache. IDistributedCache does not provide atomic compare-and-swap:
/// cross-region consistency, store failover and multi-node guarantees still
/// require provider-specific integration tests before production deployment.
/// </summary>
public sealed class DistributedBffTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider) : ITicketStore
{
    internal const string AbsoluteExpiresProperty = ".bff.absolute_expires_utc";
    internal const string RevocationPrefix = "foundation:bff:revoked:v1:";

    // A fixed absolute ceiling is essential: a tombstone must outlive every
    // ticket ever renewed under a revoked session key. Sliding cookie renewal
    // may extend idle expiry but MUST NOT extend this original session ceiling.
    internal static readonly TimeSpan MaximumSessionLifetime = TimeSpan.FromHours(24);
    internal static readonly TimeSpan RevocationRetention = TimeSpan.FromHours(48);

    private static readonly byte[] RevokedMarker = [1];

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Foundation", "Bff", "TicketStore", "v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket)
        => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var now = DateTimeOffset.UtcNow;
        var issued = ticket.Properties.IssuedUtc ?? now;
        if (issued > now.AddMinutes(5))
            throw new InvalidOperationException("Cannot issue a BFF session in the future.");

        var absolute = issued.Add(MaximumSessionLifetime);
        if (absolute <= now)
            throw new InvalidOperationException("The absolute BFF session lifetime has elapsed.");

        ticket.Properties.IssuedUtc = issued;
        ticket.Properties.Items[AbsoluteExpiresProperty] =
            absolute.ToString("O", CultureInfo.InvariantCulture);

        var key = BffDefaults.TicketCachePrefix +
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        await WriteTicketAsync(key, ticket, absolute, cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
        => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(
        string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(ticket);

        if (!TryGetAbsoluteExpiry(ticket, out var absolute))
            throw new InvalidOperationException(
                "BFF renewal requires the original protected absolute session expiry.");

        if (absolute <= DateTimeOffset.UtcNow)
            return;

        // Reject non-existent, expired and explicitly revoked session keys.
        // This read alone is not a lock: revocation markers below make the
        // concurrent SetAsync harmless to all subsequent authentication.
        var existing = await RetrieveAsync(key, cancellationToken);
        if (existing is null ||
            !TryGetAbsoluteExpiry(existing, out var storedAbsolute) ||
            storedAbsolute != absolute)
            return;

        if (await IsRevokedAsync(key, cancellationToken))
            return;

        await WriteTicketAsync(key, ticket, absolute, cancellationToken);

        // If logout won the race while SetAsync was in flight, delete the
        // stale write. Reads ALSO check the tombstone, so even during this
        // window it must not authenticate a revoked session.
        if (await IsRevokedAsync(key, cancellationToken))
            await cache.RemoveAsync(key, cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
        => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key) || await IsRevokedAsync(key, cancellationToken))
            return null;

        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is null)
            return null;

        try
        {
            var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
            var now = DateTimeOffset.UtcNow;
            if (ticket?.Properties.ExpiresUtc is not { } expiry ||
                expiry <= now ||
                !TryGetAbsoluteExpiry(ticket, out var absolute) ||
                absolute <= now ||
                expiry > absolute)
            {
                // A legacy ticket without a stable absolute expiry fails
                // closed and cannot be renewed indefinitely.
                await cache.RemoveAsync(key, cancellationToken);
                return null;
            }

            // Logout may begin while a cache read is in progress.
            return await IsRevokedAsync(key, cancellationToken) ? null : ticket;
        }
        catch (CryptographicException)
        {
            // Corrupt/foreign ticket is never trusted.
            return null;
        }
    }

    public Task RemoveAsync(string key)
        => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
            return;

        // Write the tombstone FIRST. If cache removal races a renewal write,
        // the marker still prevents future reads and renewals on this key.
        // If marker persistence fails, logout fails closed instead of
        // reporting success while the server ticket remains usable.
        await cache.SetAsync(RevocationKey(key), RevokedMarker,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = RevocationRetention
            }, cancellationToken);

        await cache.RemoveAsync(key, cancellationToken);
    }

    private async Task WriteTicketAsync(
        string key, AuthenticationTicket ticket,
        DateTimeOffset absolute, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = ticket.Properties.ExpiresUtc
            ?? throw new InvalidOperationException(
                "BFF session tickets require an ExpiresUtc value.");

        if (expires <= now || absolute <= now)
            throw new InvalidOperationException("Cannot persist an expired BFF session ticket.");

        if (expires > absolute)
        {
            ticket.Properties.ExpiresUtc = absolute;
            expires = absolute;
        }

        var protectedBytes = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await cache.SetAsync(key, protectedBytes,
            new DistributedCacheEntryOptions { AbsoluteExpiration = expires },
            cancellationToken);
    }

    private Task<bool> IsRevokedAsync(string key, CancellationToken cancellationToken)
        => IsRevokedCoreAsync(key, cancellationToken);

    private async Task<bool> IsRevokedCoreAsync(
        string key, CancellationToken cancellationToken)
        => await cache.GetAsync(RevocationKey(key), cancellationToken) is not null;

    private static bool TryGetAbsoluteExpiry(
        AuthenticationTicket ticket, out DateTimeOffset absolute)
    {
        absolute = default;
        return ticket.Properties.Items.TryGetValue(AbsoluteExpiresProperty, out var raw) &&
            DateTimeOffset.TryParseExact(
                raw, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out absolute);
    }

    private static string RevocationKey(string key)
        => RevocationPrefix + key[BffDefaults.TicketCachePrefix.Length..];

    private static bool IsValidKey(string? key)
        => !string.IsNullOrWhiteSpace(key) &&
           key.StartsWith(BffDefaults.TicketCachePrefix, StringComparison.Ordinal) &&
           key.Length == BffDefaults.TicketCachePrefix.Length + 43 &&
           key.AsSpan(BffDefaults.TicketCachePrefix.Length).IndexOfAnyExcept(
               "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;

    private static void ValidateKey(string key)
    {
        if (!IsValidKey(key))
            throw new ArgumentException("Invalid BFF session lookup key.", nameof(key));
    }
}
