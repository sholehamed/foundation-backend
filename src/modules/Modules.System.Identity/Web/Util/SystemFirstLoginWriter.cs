using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

public sealed class SystemFirstLoginValidationException : Exception;
public sealed class SystemFirstLoginTargetException : Exception;

/// <summary>
/// A System-scoped password-change proof, separate from Tenant invitations.
/// A pending System User cannot log in, receive OAuth tokens, or use BFF until
/// the one-use challenge activates the account with a *different* password.
/// </summary>
public static class SystemFirstLoginWriter
{
    public sealed record IssueResult(string TemporaryPassword, DateTime ExpiresAtUtc);
    public sealed record StartRequest(string? UserName, string? TemporaryPassword);
    public sealed record LoginDecision(
        string Status, string? Challenge = null, DateTime? ExpiresAtUtc = null);
    public sealed record CompleteRequest(string? Challenge, string? NewPassword);

    public sealed class TemporaryCredentialRow
    {
        public Guid Id { get; set; }
        public Guid TargetUserId { get; set; }
        public string PasswordHash { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public int FailedAttempts { get; set; }
    }

    public sealed class ChallengeRow
    {
        public Guid Id { get; set; }
        public Guid TargetUserId { get; set; }
        public Guid TemporaryCredentialId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private static readonly Regex Username = new(
        @"\A[a-zA-Z0-9][a-zA-Z0-9._+\-]{2,99}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HexChallenge = new(
        @"\A[A-F0-9]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool LooksLikeIssuedPassword(string? value)
    {
        if (value is not { Length: 27 }) return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (i is 6 or 13 or 20)
            {
                if (value[i] != '-') return false;
            }
            else if (!Alphabet.Contains(value[i]))
                return false;
        }
        return true;
    }

    private static string GeneratePassword()
    {
        Span<char> chars = stackalloc char[27];
        for (var index = 0; index < 24; index++)
        {
            var position = index + index / 6;
            chars[position] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            if (index is 5 or 11 or 17) chars[position + 1] = '-';
        }
        return new string(chars);
    }

    private static Task<bool> HasAnyGrantsAsync(
        IdentityDbContext db, Guid userId, CancellationToken ct)
        => HasAnyGrantsCoreAsync(db, userId, ct);

    private static async Task<bool> HasAnyGrantsCoreAsync(
        IdentityDbContext db, Guid id, CancellationToken ct)
    {
        var master = DefaultSeedData.MasterTenant.Id;
        // Reject even a stray foreign relationship. A new inert account must
        // have no privilege, including an assignment added by another writer.
        return await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                   .AnyAsync(x => x.UserId == id, ct) ||
               await db.UserGroups.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                   .AnyAsync(x => x.UserId == id, ct) ||
               await db.UserPermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                   .AnyAsync(x => x.UserId == id, ct);
    }

    private static Task AuditAsync(IdentityDbContext db,
        Guid actorId, Guid targetId, string action, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[SystemAccountLifecycleAuditEntries]
                ([Id],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
            VALUES ({Guid.NewGuid()},{actorId},{targetId},{action},SYSUTCDATETIME())
            """, ct);

    /// <summary>
    /// Only an active live Master SuperAdmin can generate a one-time password
    /// for a newly created *inactive* and fully unprivileged System target.
    /// This secret is returned ONCE over HTTPS and must never be logged.
    /// </summary>
    public static async Task<IssueResult> IssueAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        IPasswordHasher<User> hasher, ClaimsPrincipal actor, Guid targetId,
        bool enabled, CancellationToken ct = default)
    {
        if (!enabled || !db.Database.IsSqlServer() || targetId == Guid.Empty ||
            !Guid.TryParse(actor.FindFirstValue(OpenIddictConstants.Claims.Subject), out var actorId) ||
            actorId == targetId)
            throw new UnauthorizedAccessException("System first-login issuance is not authorized.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            using var authorized = await gateway.BeginAsync(actor, cancellationToken: ct);

            var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(u =>
                u.Id == targetId && u.UserType == UserType.System &&
                u.TenantId == null && !u.IsActive, ct);
            if (target is null || await HasAnyGrantsAsync(db, targetId, ct))
                throw new SystemFirstLoginTargetException();

            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(60);
            var secret = GeneratePassword();
            var passwordHash = hasher.HashPassword(target, secret);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[SystemAccountTemporaryCredentials]
                SET [RevokedAtUtc]={now}
                WHERE [TargetUserId]={targetId} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[SystemAccountFirstLoginChallenges]
                SET [RevokedAtUtc]={now}
                WHERE [TargetUserId]={targetId} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[SystemAccountTemporaryCredentials]
                    ([Id],[TargetUserId],[IssuedByUserId],[PasswordHash],
                     [CreatedAtUtc],[ExpiresAtUtc],[FailedAttempts])
                VALUES ({Guid.NewGuid()},{targetId},{actorId},{passwordHash},
                        {now},{expires},{0})
                """, ct);
            await AuditAsync(db, actorId, targetId, "temp_issued", ct);
            await tx.CommitAsync(ct);
            return new IssueResult(secret, expires);
        });
    }

    /// <summary>
    /// A normal system password always follows the untouched OAuth login.
    /// Unknown/expired initial credentials return only "continue_login",
    /// preventing an account-existence oracle.
    /// </summary>
    public static async Task<LoginDecision> ResolveForLoginAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        StartRequest? request, CancellationToken ct = default)
    {
        if (!LooksLikeIssuedPassword(request?.TemporaryPassword))
            return new LoginDecision("continue_login");
        try
        {
            var started = await StartAsync(db, hasher, request, ct);
            return new LoginDecision("password_change_required",
                started.Challenge, started.ExpiresAtUtc);
        }
        catch (SystemFirstLoginValidationException)
        {
            return new LoginDecision("continue_login");
        }
    }

    public sealed record StartResult(string Challenge, DateTime ExpiresAtUtc);

    public static async Task<StartResult> StartAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        StartRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() || request is null ||
            request.UserName is not { Length: >= 3 and <= 100 } rawName ||
            !Username.IsMatch(rawName.Trim()) ||
            !LooksLikeIssuedPassword(request.TemporaryPassword))
            throw new SystemFirstLoginValidationException();

        var normalized = rawName.Trim().ToUpperInvariant();
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await db.Tenants.AsNoTracking().AnyAsync(x =>
                    x.Id == DefaultSeedData.MasterTenant.Id && x.IsActive, ct))
                return (StartResult?)null;
            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.UserType == UserType.System && u.TenantId == null &&
                u.NormalizedUserName == normalized && !u.IsActive, ct);
            if (user is null || await HasAnyGrantsAsync(db, user.Id, ct))
                return (StartResult?)null;

            var rows = await db.Database.SqlQueryRaw<TemporaryCredentialRow>(
                """
                SELECT TOP(1) [Id],[TargetUserId],[PasswordHash],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc],[FailedAttempts]
                FROM [dbo].[SystemAccountTemporaryCredentials] WITH (UPDLOCK,HOLDLOCK)
                WHERE [TargetUserId]={0}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                ORDER BY [CreatedAtUtc] DESC
                """, user.Id).ToArrayAsync(ct);
            var credential = rows.SingleOrDefault();
            var now = DateTime.UtcNow;
            if (credential is null || credential.ExpiresAtUtc <= now ||
                credential.FailedAttempts >= 5)
                return (StartResult?)null;

            if (hasher.VerifyHashedPassword(
                    user, credential.PasswordHash, request.TemporaryPassword!) ==
                PasswordVerificationResult.Failed)
            {
                // A wrong guess is durable even though proof issuance failed.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [dbo].[SystemAccountTemporaryCredentials]
                    SET [FailedAttempts]=[FailedAttempts]+1
                    WHERE [Id]={credential.Id}
                    """, ct);
                await tx.CommitAsync(ct);
                return (StartResult?)null;
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[SystemAccountFirstLoginChallenges]
                SET [RevokedAtUtc]={now}
                WHERE [TargetUserId]={user.Id} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL
                """, ct);
            var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var tokenHash = SHA256.HashData(Convert.FromHexString(challenge));
            var expiry = now.AddMinutes(10);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[SystemAccountFirstLoginChallenges]
                    ([Id],[TargetUserId],[TemporaryCredentialId],[TokenHash],
                     [CreatedAtUtc],[ExpiresAtUtc])
                VALUES ({Guid.NewGuid()},{user.Id},{credential.Id},{tokenHash},
                    {now},{expiry})
                """, ct);
            await AuditAsync(db, user.Id, user.Id, "temp_verified", ct);
            await tx.CommitAsync(ct);
            return new StartResult(challenge, expiry);
        });
        return result ?? throw new SystemFirstLoginValidationException();
    }

    public static async Task CompleteAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        CompleteRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() || request is null ||
            request.Challenge is not { Length: 64 } rawChallenge ||
            !HexChallenge.IsMatch(rawChallenge) ||
            request.NewPassword is not { Length: >= 15 and <= 128 } newPassword ||
            newPassword.Any(char.IsControl))
            throw new SystemFirstLoginValidationException();

        var tokenHash = SHA256.HashData(Convert.FromHexString(rawChallenge));
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;
            var matches = await db.Database.SqlQueryRaw<ChallengeRow>(
                """
                SELECT [Id],[TargetUserId],[TemporaryCredentialId],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc]
                FROM [dbo].[SystemAccountFirstLoginChallenges] WITH (UPDLOCK,HOLDLOCK)
                WHERE [TokenHash]={0}
                """, tokenHash).ToArrayAsync(ct);
            var challenge = matches.SingleOrDefault();
            if (challenge is null || challenge.UsedAtUtc is not null ||
                challenge.RevokedAtUtc is not null || challenge.ExpiresAtUtc <= now)
                throw new SystemFirstLoginValidationException();

            var credentials = await db.Database.SqlQueryRaw<TemporaryCredentialRow>(
                """
                SELECT [Id],[TargetUserId],[PasswordHash],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc],[FailedAttempts]
                FROM [dbo].[SystemAccountTemporaryCredentials] WITH (UPDLOCK,HOLDLOCK)
                WHERE [Id]={0}
                """, challenge.TemporaryCredentialId).ToArrayAsync(ct);
            var credential = credentials.SingleOrDefault();
            if (credential is null || credential.TargetUserId != challenge.TargetUserId ||
                credential.UsedAtUtc is not null || credential.RevokedAtUtc is not null ||
                credential.ExpiresAtUtc <= now || credential.FailedAttempts >= 5)
                throw new SystemFirstLoginValidationException();

            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.Id == challenge.TargetUserId && u.TenantId == null &&
                u.UserType == UserType.System && !u.IsActive, ct);
            if (user is null || await HasAnyGrantsAsync(db, user.Id, ct) ||
                !await db.Tenants.AsNoTracking().AnyAsync(t =>
                    t.Id == DefaultSeedData.MasterTenant.Id && t.IsActive, ct))
                throw new SystemFirstLoginValidationException();

            if (newPassword.Contains(user.UserName, StringComparison.OrdinalIgnoreCase) ||
                hasher.VerifyHashedPassword(user, credential.PasswordHash, newPassword) !=
                    PasswordVerificationResult.Failed ||
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "password123456789", "123456789012345", "qwerty123456789" }
                    .Contains(newPassword))
                throw new SystemFirstLoginValidationException();

            user.PasswordHash = hasher.HashPassword(user, newPassword);
            user.IsActive = true;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.IsLockedOut = false;
            user.LockoutEndUtc = null;
            user.AccessFailedCount = 0;
            user.LastModifiedAt = now;
            await db.SaveChangesAsync(ct);

            var consumed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[SystemAccountFirstLoginChallenges]
                SET [UsedAtUtc]={now}
                WHERE [Id]={challenge.Id} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL AND [ExpiresAtUtc]>{now}
                """, ct);
            var used = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[SystemAccountTemporaryCredentials]
                SET [UsedAtUtc]={now}
                WHERE [Id]={credential.Id} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL AND [ExpiresAtUtc]>{now}
                """, ct);
            if (consumed != 1 || used != 1)
                throw new SystemFirstLoginValidationException();

            await AuditAsync(db, user.Id, user.Id, "temp_activated", ct);
            await tx.CommitAsync(ct);
            // No cookie, token or authorized session: caller must start a
            // fresh normal login with the new permanent password.
        });
    }
}
