using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class FirstLoginDeniedException : Exception { }
public sealed class FirstLoginValidationException : Exception { }
public sealed class FirstLoginTargetException : Exception { }

/// <summary>
/// FR-13B-3: separate initial credential from the User's real password.
/// No unrestricted authentication/token is ever issued while IsActive=false.
/// Password-change challenge is not an OAuth credential and works ONLY here.
/// </summary>
public static class TenantFirstLoginWriter
{
    public sealed record IssueResult(string TemporaryPassword, DateTime ExpiresAtUtc);
    public sealed record StartRequest(string? TenantSlug, string? UserName, string? TemporaryPassword);
    public sealed record StartResult(string Challenge, DateTime ExpiresAtUtc, bool RequiresPasswordChange);
    public sealed record CompleteRequest(string? TenantSlug, string? Challenge, string? NewPassword);

    public sealed class TempRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
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
        public Guid TenantId { get; set; }
        public Guid TargetUserId { get; set; }
        public Guid TemporaryCredentialId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private static readonly Regex Slug = new(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<bool> CanIssueAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;

        var registered = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.MembersIssueTempPassword &&
                p.IsActive && p.ResourceId != null && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                db.PermissionUiResources.Count(l => l.PermissionId == p.Id) == 1 &&
                db.PermissionUiResources.Any(l =>
                    l.PermissionId == p.Id && l.UiResource != null &&
                    l.UiResource.IsActive && l.UiResource.ResourceId == p.ResourceId &&
                    l.UiResource.Key == "tenant.workspace.members.issue-temp-password" &&
                    l.UiResource.Type == UiResourceType.ACTION &&
                    l.UiResource.Description == null))
            .Select(p => p.Id).ToArrayAsync(ct);
        return registered.Length == 1 &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersIssueTempPassword, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    private static async Task<User> EligibleTargetAsync(
        ITenantContext current, IdentityDbContext db, Guid memberId, CancellationToken ct)
    {
        if (current.UserId == memberId || memberId == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId)
            throw new FirstLoginTargetException();
        var target = await db.Users.SingleOrDefaultAsync(x =>
            x.Id == memberId && x.TenantId == tenantId &&
            x.UserType == UserType.Tenant && !x.IsActive, ct);
        if (target is null ||
            await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking().AnyAsync(ur =>
                ur.TenantId == tenantId && ur.UserId == memberId &&
                ur.Role.TenantId == tenantId &&
                ur.Role.NormalizedName == "OWNER" && ur.Role.Scope != RoleScope.System, ct))
            throw new FirstLoginTargetException();
        return target;
    }

    private static string GenerateTemporaryPassword()
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

    /// <summary>
    /// The only method allowed to return plaintext initial credentials.
    /// Caller must emit no-store, not log body, and show password to Owner ONCE.
    /// </summary>
    public static async Task<IssueResult> IssueAsync(
        ITenantContext current, IdentityDbContext db,
        IPasswordHasher<User> hasher, Guid memberId, CancellationToken ct = default)
    {
        if (!await CanIssueAsync(current, db, ct)) throw new FirstLoginDeniedException();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await CanIssueAsync(current, db, ct)) throw new FirstLoginDeniedException();
            var target = await EligibleTargetAsync(current, db, memberId, ct);
            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            var now = DateTime.UtcNow;
            var expiry = now.AddMinutes(60);
            var password = GenerateTemporaryPassword();
            var hashed = hasher.HashPassword(target, password);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberTemporaryCredentials]
                SET [RevokedAtUtc] = {now}
                WHERE [TenantId] = {tenantId} AND [TargetUserId] = {memberId}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberFirstLoginChallenges]
                SET [RevokedAtUtc] = {now}
                WHERE [TenantId] = {tenantId} AND [TargetUserId] = {memberId}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            // An earlier email invitation cannot activate a changed first-login
            // credential behind Owner's back. SMTP invitation remains available
            // as a separate explicit option when reissued later.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberInvitations]
                SET [RevokedAtUtc] = {now}
                WHERE [TenantId] = {tenantId} AND [TargetUserId] = {memberId}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberTemporaryCredentials]
                    ([Id],[TenantId],[TargetUserId],[IssuedByUserId],
                     [PasswordHash],[CreatedAtUtc],[ExpiresAtUtc],[FailedAttempts])
                VALUES ({Guid.NewGuid()},{tenantId},{memberId},{actor},
                        {hashed},{now},{expiry},{0})
                """, ct);
            await AuditAsync(db, tenantId, actor, memberId, "temp_issued", ct);
            await tx.CommitAsync(ct);
            return new IssueResult(password, expiry);
        });
    }

    public static async Task<StartResult> StartAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        StartRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() || request is null ||
            request.TenantSlug is not { Length: >= 2 and <= 63 } slug || !Slug.IsMatch(slug) ||
            request.UserName is not { Length: >= 3 and <= 100 } name ||
            request.TemporaryPassword is not { Length: >= 20 and <= 64 } password)
            throw new FirstLoginValidationException();

        var normalizedName = name.Trim().ToUpperInvariant();
        var strategy = db.Database.CreateExecutionStrategy();
        // A wrong attempt must COMMIT the failed-attempt counter before
        // reporting the same generic failure as an unknown account.
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var tenant = await db.Tenants.AsNoTracking().Where(t => t.Slug == slug && t.IsActive)
                .Select(t => t.Id).SingleOrDefaultAsync(ct);
            var user = tenant == Guid.Empty ? null : await db.Users.SingleOrDefaultAsync(u =>
                u.TenantId == tenant && u.UserType == UserType.Tenant &&
                u.NormalizedUserName == normalizedName && !u.IsActive, ct);
            if (user is null) return (StartResult?)null;

            var rows = await db.Database.SqlQueryRaw<TempRow>(
                """
                SELECT TOP (1) [Id],[TenantId],[TargetUserId],[PasswordHash],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc],[FailedAttempts]
                FROM [dbo].[TenantMemberTemporaryCredentials] WITH (UPDLOCK,HOLDLOCK)
                WHERE [TenantId]={0} AND [TargetUserId]={1}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                ORDER BY [CreatedAtUtc] DESC
                """, tenant, user.Id).ToArrayAsync(ct);
            var row = rows.SingleOrDefault();
            var now = DateTime.UtcNow;
            if (row is null || row.ExpiresAtUtc <= now || row.FailedAttempts >= 5)
                return (StartResult?)null;

            var check = hasher.VerifyHashedPassword(user, row.PasswordHash, password);
            if (check == PasswordVerificationResult.Failed)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [dbo].[TenantMemberTemporaryCredentials]
                    SET [FailedAttempts]=[FailedAttempts]+1
                    WHERE [Id]={row.Id}
                    """, ct);
                await tx.CommitAsync(ct);
                return (StartResult?)null;
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberFirstLoginChallenges]
                SET [RevokedAtUtc]={now}
                WHERE [TenantId]={tenant} AND [TargetUserId]={user.Id}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hash = SHA256.HashData(Convert.FromHexString(challenge));
            var expires = now.AddMinutes(10);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberFirstLoginChallenges]
                    ([Id],[TenantId],[TargetUserId],[TemporaryCredentialId],
                     [TokenHash],[CreatedAtUtc],[ExpiresAtUtc])
                VALUES ({Guid.NewGuid()},{tenant},{user.Id},{row.Id},
                    {hash},{now},{expires})
                """, ct);
            await AuditAsync(db, tenant, user.Id, user.Id, "temp_verified", ct);
            await tx.CommitAsync(ct);
            return new StartResult(challenge, expires, true);
        });
        return result ?? throw new FirstLoginValidationException();
    }

    public static async Task CompleteAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        CompleteRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() || request is null ||
            request.TenantSlug is not { Length: >= 2 and <= 63 } slug || !Slug.IsMatch(slug) ||
            request.Challenge is not { Length: 64 } token ||
            !Regex.IsMatch(token, @"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant) ||
            request.NewPassword is not { Length: >= 15 and <= 128 } newPassword ||
            newPassword.Any(char.IsControl))
            throw new FirstLoginValidationException();

        var tokenHash = SHA256.HashData(Convert.FromHexString(token));
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;
            var matches = await db.Database.SqlQueryRaw<ChallengeRow>(
                """
                SELECT [Id],[TenantId],[TargetUserId],[TemporaryCredentialId],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc]
                FROM [dbo].[TenantMemberFirstLoginChallenges] WITH (UPDLOCK,HOLDLOCK)
                WHERE [TokenHash]={0}
                """, tokenHash).ToArrayAsync(ct);
            var ch = matches.SingleOrDefault();
            if (ch is null || ch.UsedAtUtc is not null || ch.RevokedAtUtc is not null ||
                ch.ExpiresAtUtc <= now) throw new FirstLoginValidationException();

            var tenantValid = await db.Tenants.AsNoTracking().AnyAsync(t =>
                t.Id == ch.TenantId && t.Slug == slug && t.IsActive, ct);
            if (!tenantValid) throw new FirstLoginValidationException();

            var credentials = await db.Database.SqlQueryRaw<TempRow>(
                """
                SELECT [Id],[TenantId],[TargetUserId],[PasswordHash],
                    [ExpiresAtUtc],[UsedAtUtc],[RevokedAtUtc],[FailedAttempts]
                FROM [dbo].[TenantMemberTemporaryCredentials] WITH (UPDLOCK,HOLDLOCK)
                WHERE [Id]={0}
                """, ch.TemporaryCredentialId).ToArrayAsync(ct);
            var cred = credentials.SingleOrDefault();
            if (cred is null || cred.TenantId != ch.TenantId ||
                cred.TargetUserId != ch.TargetUserId || cred.UsedAtUtc is not null ||
                cred.RevokedAtUtc is not null || cred.ExpiresAtUtc <= now ||
                cred.FailedAttempts >= 5) throw new FirstLoginValidationException();

            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.Id == ch.TargetUserId && u.TenantId == ch.TenantId &&
                u.UserType == UserType.Tenant && !u.IsActive, ct);
            if (user is null || await db.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(ur => ur.TenantId == ch.TenantId &&
                    ur.UserId == user.Id && ur.Role.TenantId == ch.TenantId &&
                    ur.Role.NormalizedName == "OWNER" && ur.Role.Scope != RoleScope.System, ct))
                throw new FirstLoginValidationException();

            if (newPassword.Contains(user.UserName, StringComparison.OrdinalIgnoreCase) ||
                hasher.VerifyHashedPassword(user, cred.PasswordHash, newPassword) !=
                    PasswordVerificationResult.Failed ||
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "password123456789", "123456789012345", "qwerty123456789" }
                    .Contains(newPassword))
                throw new FirstLoginValidationException();

            user.PasswordHash = hasher.HashPassword(user, newPassword);
            user.IsActive = true;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.IsLockedOut = false;
            user.LockoutEndUtc = null;
            user.AccessFailedCount = 0;
            user.LastModifiedAt = now;
            await db.SaveChangesAsync(ct);

            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberFirstLoginChallenges]
                SET [UsedAtUtc]={now}
                WHERE [Id]={ch.Id} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL AND [ExpiresAtUtc]>{now}
                """, ct);
            var used = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberTemporaryCredentials]
                SET [UsedAtUtc]={now}
                WHERE [Id]={cred.Id} AND [UsedAtUtc] IS NULL
                  AND [RevokedAtUtc] IS NULL AND [ExpiresAtUtc]>{now}
                """, ct);
            if (affected != 1 || used != 1) throw new FirstLoginValidationException();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberInvitations]
                SET [RevokedAtUtc]={now}
                WHERE [TenantId]={ch.TenantId} AND [TargetUserId]={user.Id}
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            await AuditAsync(db, ch.TenantId, user.Id, user.Id, "temp_activated", ct);
            await tx.CommitAsync(ct);
            // No auth session or normal OAuth token is created. Caller must
            // redirect to ordinary login and authenticate with the NEW password.
        });
    }

    private static Task AuditAsync(IdentityDbContext db, Guid tenantId,
        Guid actorId, Guid targetId, string action, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                ([Id],[TenantId],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
            VALUES ({Guid.NewGuid()},{tenantId},{actorId},{targetId},
                    {action},SYSUTCDATETIME())
            """, ct);
}
