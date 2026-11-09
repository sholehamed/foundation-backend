using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

public sealed class SystemAccountMutationValidationException : Exception { }
public sealed class SystemAccountMutationConflictException : Exception { }

/// <summary>
/// FR-14B-3: explicit, System-only profile, suspension/resumption and lock
/// lifecycle. No generic User mapping, password mutation or client TenantId.
/// Uses raw SQL version CAS + transaction-bound append-only audit, not an
/// unconfigured EF concurrency stamp. The privileged gateway revalidates the
/// actor immediately before the mutation within the same serializable tx.
/// </summary>
public static class SystemAccountLifecycleWriter
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ProfileRequest(
        string? FirstName, string? LastName, string? Email, string? ExpectedVersion);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StateRequest([property: JsonRequired] bool IsActive, string? ExpectedVersion);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record LockRequest([property: JsonRequired] bool IsLockedOut, string? ExpectedVersion);

    public sealed record AccountView(
        Guid Id, string UserName, string? Email, string? FirstName, string? LastName,
        bool IsActive, bool IsLockedOut, string Version);

    private sealed record Fields(string FirstName, string LastName, string Email,
        string NormalizedEmail);

    private static Fields Validate(ProfileRequest? input)
    {
        if (input is null) throw new SystemAccountMutationValidationException();
        var first = input.FirstName?.Trim() ?? "";
        var last = input.LastName?.Trim() ?? "";
        var email = input.Email?.Trim() ?? "";
        if (first.Length is < 1 or > 80 || last.Length is < 1 or > 80 ||
            first.Any(char.IsControl) || last.Any(char.IsControl) ||
            email.Length is < 5 or > 256 || email.Any(char.IsControl) ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new SystemAccountMutationValidationException();

        return new Fields(first, last, email, email.ToUpperInvariant());
    }

    private static void RequireVersion(string? expectedVersion)
    {
        // ConcurrencyStamp is a version token, NOT an authentication stamp.
        // Preexisting legacy users without a version must be repaired via
        // independently reviewed migration, not by this privileged HTTP API.
        if (string.IsNullOrWhiteSpace(expectedVersion) ||
            expectedVersion.Length > 128 || expectedVersion.Any(char.IsControl))
            throw new SystemAccountMutationValidationException();
    }

    private static Guid ActorId(ClaimsPrincipal actor, Guid targetId)
    {
        if (!Guid.TryParse(actor.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var id) || id == targetId || targetId == Guid.Empty)
            throw new UnauthorizedAccessException("Self or missing account mutation is forbidden.");
        return id;
    }

    private static async Task<User> TargetAsync(
        IdentityDbContext db, Guid targetId, CancellationToken ct)
    {
        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == targetId && x.UserType == UserType.System &&
            x.TenantId == null, ct);
        if (target is null)
            throw new UnauthorizedAccessException("Target is not a System account.");

        var directReserved = await db.UserRoles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x => x.UserId == targetId &&
                x.Role.NormalizedName == "SUPERADMIN", ct);
        var viaGroup = await db.UserGroups.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x => x.UserId == targetId &&
                db.GroupRoles.Any(gr =>
                    gr.GroupId == x.GroupId && gr.Role.NormalizedName == "SUPERADMIN"), ct);
        if (directReserved || viaGroup)
            throw new UnauthorizedAccessException(
                "Reserved SuperAdmin accounts require independently reviewed bootstrap.");
        return target;
    }

    private static AccountView View(User user) => new(
        user.Id, user.UserName, user.Email, user.FirstName, user.LastName,
        user.IsActive, user.IsLockedOut, user.ConcurrencyStamp!);

    public static async Task<AccountView> ReadAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        ClaimsPrincipal actor, Guid targetId, bool enabled,
        CancellationToken ct = default)
    {
        if (!enabled || !db.Database.IsSqlServer())
            throw new UnauthorizedAccessException();
        _ = ActorId(actor, targetId);
        using var scope = await gateway.BeginAsync(actor, cancellationToken: ct);
        return View(await TargetAsync(db, targetId, ct));
    }

    private static Task AuditAsync(IdentityDbContext db, Guid actorId,
        User previous, string action, string fields, string newVersion,
        bool isActive, bool locked, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[SystemAccountLifecycleAuditEntries]
                ([Id],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc],
                 [ChangedFields],[PreviousIsActive],[CurrentIsActive],
                 [PreviousIsLockedOut],[CurrentIsLockedOut],
                 [PreviousVersion],[CurrentVersion])
            VALUES ({Guid.NewGuid()},{actorId},{previous.Id},{action},
                SYSUTCDATETIME(),{fields},{previous.IsActive},{isActive},
                {previous.IsLockedOut},{locked},
                {previous.ConcurrencyStamp},{newVersion})
            """, ct);

    private static async Task RevokePendingProofsAsync(
        IdentityDbContext db, Guid targetId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
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
    }

    public static async Task<AccountView> ProfileAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        ClaimsPrincipal actor, Guid targetId, ProfileRequest? input,
        bool enabled, CancellationToken ct = default)
    {
        if (!enabled || !db.Database.IsSqlServer())
            throw new UnauthorizedAccessException();
        var actorId = ActorId(actor, targetId);
        var fields = Validate(input);
        RequireVersion(input!.ExpectedVersion);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            using var authorized = await gateway.BeginAsync(actor, cancellationToken: ct);
            var target = await TargetAsync(db, targetId, ct);
            if (!string.Equals(target.ConcurrencyStamp, input.ExpectedVersion,
                    StringComparison.Ordinal))
                throw new SystemAccountMutationConflictException();

            var changed = new List<string>();
            if (target.FirstName != fields.FirstName) changed.Add("firstName");
            if (target.LastName != fields.LastName) changed.Add("lastName");
            if (target.Email != fields.Email) changed.Add("email");
            if (changed.Count == 0)
            {
                await tx.CommitAsync(ct);
                return View(target);
            }

            var version = Guid.NewGuid().ToString("N");
            // Claims/email may have changed; invalidate all prior sessions.
            var stamp = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var modified = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Users]
                SET [FirstName]={fields.FirstName}, [LastName]={fields.LastName},
                    [Email]={fields.Email}, [NormalizedEmail]={fields.NormalizedEmail},
                    [EmailConfirmed]={target.Email == fields.Email && target.EmailConfirmed},
                    [SecurityStamp]={stamp},
                    [ConcurrencyStamp]={version}, [LastModifiedAt]={now}
                WHERE [Id]={target.Id} AND [TenantId] IS NULL AND [UserType]={(byte)UserType.System}
                  AND [ConcurrencyStamp]={input.ExpectedVersion}
                """, ct);
            if (modified != 1) throw new SystemAccountMutationConflictException();
            if (!target.IsActive)
                await RevokePendingProofsAsync(db, target.Id, ct);
            await AuditAsync(db, actorId, target, "profile_updated",
                string.Join(",", changed), version, target.IsActive, target.IsLockedOut, ct);
            await tx.CommitAsync(ct);
            return new AccountView(target.Id, target.UserName, fields.Email,
                fields.FirstName, fields.LastName, target.IsActive, target.IsLockedOut, version);
        });
    }

    public static async Task<AccountView> StateAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        ClaimsPrincipal actor, Guid targetId, StateRequest? input,
        bool enabled, CancellationToken ct = default)
    {
        if (!enabled || !db.Database.IsSqlServer())
            throw new UnauthorizedAccessException();
        var actorId = ActorId(actor, targetId);
        RequireVersion(input?.ExpectedVersion);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            using var authorized = await gateway.BeginAsync(actor, cancellationToken: ct);
            var target = await TargetAsync(db, targetId, ct);
            if (!string.Equals(target.ConcurrencyStamp, input!.ExpectedVersion,
                    StringComparison.Ordinal))
                throw new SystemAccountMutationConflictException();

            if (target.IsActive == input.IsActive)
            {
                await tx.CommitAsync(ct);
                return View(target);
            }

            // Direct activation of a never-verified pending identity would
            // bypass FR-14B-2B. Re-activation requires prior temp_activated
            // audit proving that the permanent password was set in the
            // reviewed one-time System first-login protocol.
            if (input.IsActive && await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value] FROM [dbo].[SystemAccountLifecycleAuditEntries]
                WHERE [TargetUserId]={0} AND [Action]='temp_activated'
                """, target.Id).SingleAsync(ct) == 0)
                throw new UnauthorizedAccessException(
                    "Pending System accounts cannot be directly activated.");

            var version = Guid.NewGuid().ToString("N");
            var stamp = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Users]
                SET [IsActive]={input.IsActive}, [SecurityStamp]={stamp},
                    [ConcurrencyStamp]={version}, [LastModifiedAt]={now}
                WHERE [Id]={target.Id} AND [TenantId] IS NULL AND [UserType]={(byte)UserType.System}
                  AND [ConcurrencyStamp]={input.ExpectedVersion}
                """, ct);
            if (affected != 1) throw new SystemAccountMutationConflictException();
            if (!input.IsActive) await RevokePendingProofsAsync(db, target.Id, ct);
            await AuditAsync(db, actorId, target,
                input.IsActive ? "reactivated" : "suspended",
                "isActive", version, input.IsActive, target.IsLockedOut, ct);
            await tx.CommitAsync(ct);
            return new AccountView(target.Id, target.UserName, target.Email,
                target.FirstName, target.LastName, input.IsActive, target.IsLockedOut, version);
        });
    }

    public static async Task<AccountView> LockAsync(
        IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        ClaimsPrincipal actor, Guid targetId, LockRequest? input,
        bool enabled, CancellationToken ct = default)
    {
        if (!enabled || !db.Database.IsSqlServer())
            throw new UnauthorizedAccessException();
        var actorId = ActorId(actor, targetId);
        RequireVersion(input?.ExpectedVersion);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            using var authorized = await gateway.BeginAsync(actor, cancellationToken: ct);
            var target = await TargetAsync(db, targetId, ct);
            if (!target.IsActive)
                throw new UnauthorizedAccessException(
                    "Locking an unactivated account cannot bypass first login.");
            if (!string.Equals(target.ConcurrencyStamp, input!.ExpectedVersion,
                    StringComparison.Ordinal))
                throw new SystemAccountMutationConflictException();

            // A legacy 15-minute password-failure lock has IsLockedOut=true
            // but must not be mistaken for a durable administrator lock.
            // Setting locked=true explicitly upgrades that short lock to the
            // reviewed long-lived administrative lock.
            if (target.IsLockedOut == input.IsLockedOut &&
                (!input.IsLockedOut ||
                 target.LockoutEndUtc > DateTime.UtcNow.AddYears(10)))
            {
                await tx.CommitAsync(ct);
                return View(target);
            }

            var version = Guid.NewGuid().ToString("N");
            var stamp = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            // UserCredentialAuthenticator only denies locked users with a
            // future LockoutEndUtc. A bare IsLockedOut=true does NOT lock.
            var lockEnd = input.IsLockedOut ? now.AddYears(50) : (DateTime?)null;
            var failures = input.IsLockedOut ? target.AccessFailedCount : 0;
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Users]
                SET [IsLockedOut]={input.IsLockedOut},
                    [LockoutEndUtc]={lockEnd}, [AccessFailedCount]={failures},
                    [SecurityStamp]={stamp}, [ConcurrencyStamp]={version},
                    [LastModifiedAt]={now}
                WHERE [Id]={target.Id} AND [TenantId] IS NULL AND [UserType]={(byte)UserType.System}
                  AND [ConcurrencyStamp]={input.ExpectedVersion}
                """, ct);
            if (affected != 1) throw new SystemAccountMutationConflictException();
            if (input.IsLockedOut) await RevokePendingProofsAsync(db, target.Id, ct);
            await AuditAsync(db, actorId, target, input.IsLockedOut ? "locked" : "unlocked",
                "isLockedOut", version, target.IsActive, input.IsLockedOut, ct);
            await tx.CommitAsync(ct);
            return new AccountView(target.Id, target.UserName, target.Email,
                target.FirstName, target.LastName, target.IsActive, input.IsLockedOut, version);
        });
    }
}
