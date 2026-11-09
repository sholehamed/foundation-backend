using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;

namespace Modules.System.Identity.Web.Util;

public sealed class SystemAccountCreateValidationException(string message) : Exception(message);
public sealed class SystemAccountCreateConflictException(string message) : Exception(message);

/// <summary>
/// FR-14B-2A: create an inert System identity only. Never issue credentials,
/// copy a caller-controlled tenant, activate an account, or assign privileges.
/// Separate reviewed initial credential issuance is required before login.
/// </summary>
public static class SystemPendingAccountWriter
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateRequest(
        string? UserName, string? Email, string? FirstName, string? LastName);

    public sealed record CreatedAccount(
        Guid Id, string UserName, string Email, string FirstName, string LastName,
        bool IsActive);

    private static readonly Regex SafeUserName = new(
        @"\A[a-zA-Z0-9][a-zA-Z0-9._+\-]{2,99}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static (string UserName, string NormalizedName, string Email,
        string NormalizedEmail, string FirstName, string LastName)
        Validate(CreateRequest? input)
    {
        if (input is null)
            throw new SystemAccountCreateValidationException("Account details are required.");

        var name = input.UserName?.Trim() ?? "";
        var email = input.Email?.Trim() ?? "";
        var first = input.FirstName?.Trim() ?? "";
        var last = input.LastName?.Trim() ?? "";
        if (!SafeUserName.IsMatch(name))
            throw new SystemAccountCreateValidationException("Invalid System username.");
        if (email.Length is < 5 or > 256 || email.Any(char.IsControl) ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new SystemAccountCreateValidationException("A valid System email is required.");
        if (first.Length is < 1 or > 80 || last.Length is < 1 or > 80 ||
            first.Any(char.IsControl) || last.Any(char.IsControl))
            throw new SystemAccountCreateValidationException("Invalid account profile.");

        return (name, name.ToUpperInvariant(), email, email.ToUpperInvariant(),
            first, last);
    }

    public static async Task<CreatedAccount> CreateAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        IPasswordHasher<User> passwordHasher, ClaimsPrincipal actor,
        CreateRequest? input, bool featureEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!featureEnabled || !db.Database.IsSqlServer())
            throw new UnauthorizedAccessException(
                "System account creation is not enabled on this database.");

        var fields = Validate(input);
        var master = DefaultSeedData.MasterTenant.Id;
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            // Revalidate the live actor, Master Tenant and SuperAdmin assignment
            // in the *same* transaction as the new account and its audit row.
            using var authorized = await gateway.BeginAsync(actor,
                cancellationToken: cancellationToken);

            // The System account namespace has TenantId=null. Tenant staff
            // sharing a username/email are not an alias for a System identity.
            // SQL unique username + dedicated filtered System-email index are
            // the final backstop to concurrent duplicate creation.
            if (await db.Users.AsNoTracking().AnyAsync(u =>
                    u.UserType == UserType.System && u.TenantId == null &&
                    (u.NormalizedUserName == fields.NormalizedName ||
                     u.NormalizedEmail == fields.NormalizedEmail),
                    cancellationToken))
                throw new SystemAccountCreateConflictException(
                    "A System account with this username or email exists.");

            var account = new User(null, fields.UserName, fields.Email,
                fields.FirstName, fields.LastName, UserType.System, isActive: false)
            {
                NormalizedUserName = fields.NormalizedName,
                NormalizedEmail = fields.NormalizedEmail,
                EmailConfirmed = false,
                IsActive = false,
                IsLockedOut = false,
                AccessFailedCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };

            // The random secret is intentionally NEVER returned, stored in
            // plaintext, or sent by email. This is not an initial password.
            // Future activation must issue its own short-lived one-time proof.
            account.PasswordHash = passwordHasher.HashPassword(account,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            db.Users.Add(account);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e) when (
                e.InnerException is SqlException sql &&
                sql.Number is 2601 or 2627)
            {
                throw new SystemAccountCreateConflictException(
                    "A System account with this username or email exists.");
            }

            // Append-only SQL ledger must be committed atomically with User.
            // An audit failure rolls back the account, including on retries.
            var actorId = Guid.Parse(actor.FindFirst(
                OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject)!.Value);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[SystemAccountLifecycleAuditEntries]
                    ([Id],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
                VALUES ({Guid.NewGuid()},{actorId},{account.Id},
                    {"create_inactive"},SYSUTCDATETIME())
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CreatedAccount(account.Id, account.UserName,
                account.Email!, account.FirstName!, account.LastName!, false);
        });
    }
}
