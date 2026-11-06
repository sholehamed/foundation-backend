using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.Bff;

namespace Server.Bff;

/// <summary>
/// Durable SQL Server session store. Each UPDATE is a single SQL Server
/// transaction on the session row: renewal is UPDATE-if-active (never UPSERT),
/// and revocation updates that same row. SQL Server serializes competing row
/// writes even when separate BFF nodes serve the requests.
/// </summary>
public sealed class SqlServerBffAtomicTicketStore(
    string connectionString,
    IDataProtectionProvider dataProtectionProvider) : IBffAtomicTicketStore
{
    private const string AbsoluteExpiryProperty = ".bff.absolute_expires_utc";
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(24);
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
        var absolute = issued.Add(MaximumLifetime);
        if (absolute <= now)
            throw new InvalidOperationException("The absolute BFF session lifetime has elapsed.");
        ticket.Properties.IssuedUtc = issued;
        ticket.Properties.Items[AbsoluteExpiryProperty] =
            absolute.ToString("O", CultureInfo.InvariantCulture);
        var expiry = ClampAndValidateExpiry(ticket, absolute);
        var bytes = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        var key = BffDefaults.TicketCachePrefix +
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO dbo.FoundationBffSessions
                (SessionKey, TicketCiphertext, CreatedUtc, ExpiresUtc, AbsoluteExpiresUtc)
            VALUES (@key, @ticket, @created, @expires, @absolute);
            """, connection);
        command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
        command.Parameters.Add("@ticket", SqlDbType.VarBinary, -1).Value = bytes;
        command.Parameters.Add("@created", SqlDbType.DateTime2).Value = issued.UtcDateTime;
        command.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiry.UtcDateTime;
        command.Parameters.Add("@absolute", SqlDbType.DateTime2).Value = absolute.UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                "BFF renewal requires the original protected absolute expiry.");
        if (absolute <= DateTimeOffset.UtcNow)
            return;

        var expiry = ClampAndValidateExpiry(ticket, absolute);
        var bytes = _protector.Protect(TicketSerializer.Default.Serialize(ticket));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.FoundationBffSessions WITH (ROWLOCK)
            SET TicketCiphertext = @ticket, ExpiresUtc = @expires,
                Revision = Revision + 1
            WHERE SessionKey = @key
                AND RevokedUtc IS NULL
                AND TicketCiphertext IS NOT NULL
                AND ExpiresUtc > SYSUTCDATETIME()
                AND AbsoluteExpiresUtc > SYSUTCDATETIME()
                AND AbsoluteExpiresUtc = @absolute;
            """, connection);
        command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
        command.Parameters.Add("@ticket", SqlDbType.VarBinary, -1).Value = bytes;
        command.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiry.UtcDateTime;
        command.Parameters.Add("@absolute", SqlDbType.DateTime2).Value = absolute.UtcDateTime;
        // No matching row is a normal stale renewal: NEVER insert it again.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
        => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
            return null;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TicketCiphertext, ExpiresUtc, AbsoluteExpiresUtc
            FROM dbo.FoundationBffSessions WITH (READCOMMITTEDLOCK)
            WHERE SessionKey = @key
                AND RevokedUtc IS NULL
                AND TicketCiphertext IS NOT NULL
                AND ExpiresUtc > SYSUTCDATETIME()
                AND AbsoluteExpiresUtc > SYSUTCDATETIME();
            """, connection);
        command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var bytes = (byte[])reader[0];
        var dbExpiry = reader.GetDateTime(1);
        var dbAbsolute = reader.GetDateTime(2);
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
            if (ticket is null ||
                !TryGetAbsoluteExpiry(ticket, out var ticketAbsolute) ||
                ticket.Properties.ExpiresUtc is not { } ticketExpiry ||
                ticketExpiry.UtcDateTime != dbExpiry ||
                ticketAbsolute.UtcDateTime != dbAbsolute ||
                ticketExpiry <= DateTimeOffset.UtcNow ||
                ticketAbsolute <= DateTimeOffset.UtcNow ||
                ticketExpiry > ticketAbsolute)
                return null;
            return ticket;
        }
        catch (CryptographicException)
        {
            // A foreign/missing Data Protection key cannot grant a session.
            return null;
        }
    }

    public Task RemoveAsync(string key)
        => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
            return;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.FoundationBffSessions WITH (ROWLOCK)
            SET RevokedUtc = SYSUTCDATETIME(),
                TicketCiphertext = NULL,
                Revision = Revision + 1
            WHERE SessionKey = @key AND RevokedUtc IS NULL;
            """, connection);
        command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
        // Successful ExecuteNonQuery is a committed revocation. On DB outage
        // this throws and cannot falsely report successful logout.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset ClampAndValidateExpiry(
        AuthenticationTicket ticket, DateTimeOffset absolute)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("BFF tickets require an ExpiresUtc value.");
        if (absolute <= now || expiry <= now)
            throw new InvalidOperationException("Cannot persist an expired BFF session ticket.");
        if (expiry > absolute)
        {
            expiry = absolute;
            ticket.Properties.ExpiresUtc = expiry;
        }
        return expiry;
    }

    private static bool TryGetAbsoluteExpiry(
        AuthenticationTicket ticket, out DateTimeOffset absolute)
    {
        absolute = default;
        return ticket.Properties.Items.TryGetValue(AbsoluteExpiryProperty, out var value) &&
            DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out absolute);
    }

    private static bool IsValidKey(string? key)
        => !string.IsNullOrWhiteSpace(key) &&
            key.StartsWith(BffDefaults.TicketCachePrefix, StringComparison.Ordinal) &&
            key.Length == BffDefaults.TicketCachePrefix.Length + 43 &&
            key.AsSpan(BffDefaults.TicketCachePrefix.Length).IndexOfAnyExcept(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;

    private static void ValidateKey(string key)
    {
        if (!IsValidKey(key))
            throw new ArgumentException("Invalid BFF session key.", nameof(key));
    }
}

/// <summary>
/// Registration only. The SQL schema is a separately-reviewed deployment
/// migration, not an implicit runtime table creation.
/// </summary>
public static class BffSqlServerRegistration
{
    public static IServiceCollection AddSqlServerBffAtomicTicketStore(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BffSessions");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:BffSessions is required for SQL-backed BFF sessions.");

        services.AddSingleton<IBffAtomicTicketStore>(provider =>
            new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>()));
        return services;
    }
}
