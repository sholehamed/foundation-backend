using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;

namespace Modules.System.Bff;

/// <summary>
/// Server-side AuthenticationTicket persistence. The browser cookie contains
/// only a protected opaque lookup key; OAuth tokens never go in that cookie.
/// </summary>
public sealed class DistributedBffTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider) : ITicketStore
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Foundation", "Bff", "TicketStore", "v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket)
        => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var key = BffDefaults.TicketCachePrefix +
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket, cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
        => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(
        string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(ticket);
        var expires = ticket.Properties.ExpiresUtc
            ?? throw new InvalidOperationException(
                "BFF session tickets require an absolute ExpiresUtc value.");

        if (expires <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Cannot persist an expired BFF session ticket.");

        var serialized = TicketSerializer.Default.Serialize(ticket);
        var protectedBytes = _protector.Protect(serialized);

        await cache.SetAsync(key, protectedBytes,
            new DistributedCacheEntryOptions { AbsoluteExpiration = expires },
            cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
        => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
            return null;

        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is null) return null;

        try
        {
            var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
            if (ticket?.Properties.ExpiresUtc is not { } expiry ||
                expiry <= DateTimeOffset.UtcNow)
            {
                await cache.RemoveAsync(key, cancellationToken);
                return null;
            }

            return ticket;
        }
        catch (CryptographicException)
        {
            // Corrupt/foreign ticket is never trusted; deny session (fail closed).
            return null;
        }
    }

    public Task RemoveAsync(string key)
        => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
        => IsValidKey(key)
            ? cache.RemoveAsync(key, cancellationToken)
            : Task.CompletedTask;

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
