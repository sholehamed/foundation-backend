using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantInvitationValidationException : Exception;
public sealed class TenantInvitationTargetException : Exception;
public sealed class TenantInvitationDeliveryException : Exception;

/// <summary>
/// FR-13B-2. A token exists only in memory between random generation and the
/// configured TLS email sender. SQL holds SHA-256 of a 256-bit random token,
/// never the plaintext. No token ever reaches the Owner or a GET endpoint.
/// </summary>
public static class TenantMemberInvitationWriter
{
    public sealed record ActivateRequest(string? TenantSlug, string? Token, string? Password);
    public sealed record SubmissionReceipt(DateTime ExpiresAtUtc, string Status);

    public sealed class InvitationRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TargetUserId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }

    private static readonly Regex Slug = new(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<bool> CanInviteAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;

        // This elevation path requires the SINGLE exact reviewed ACTION link.
        // It must not inherit the legacy Owner fallback when metadata is absent.
        var registered = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.MembersInvite &&
                p.IsActive && p.ResourceId != null && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                db.PermissionUiResources.Count(l => l.PermissionId == p.Id) == 1 &&
                db.PermissionUiResources.Any(l =>
                    l.PermissionId == p.Id && l.UiResource != null &&
                    l.UiResource.IsActive && l.UiResource.ResourceId == p.ResourceId &&
                    l.UiResource.Key == "tenant.workspace.members.invite" &&
                    l.UiResource.Type == UiResourceType.ACTION &&
                    l.UiResource.Description == null))
            .Select(p => p.Id).ToArrayAsync(ct);
        if (registered.Length != 1) return false;

        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersInvite, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    private static async Task<(string Email, string TenantSlug)> TargetAsync(
        ITenantContext current, IdentityDbContext db, Guid memberId,
        CancellationToken ct)
    {
        if (current.UserId == memberId || memberId == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId)
            throw new TenantInvitationTargetException();

        var target = await db.Users.AsNoTracking().Where(x =>
                x.Id == memberId && x.TenantId == tenantId &&
                x.UserType == UserType.Tenant && !x.IsActive && !x.EmailConfirmed)
            .Select(x => new { x.Email }).SingleOrDefaultAsync(ct);
        if (target is null ||
            await db.UserRoles.AsNoTracking().AnyAsync(ur =>
                ur.TenantId == tenantId && ur.UserId == memberId &&
                ur.Role.TenantId == tenantId &&
                ur.Role.NormalizedName == "OWNER" &&
                ur.Role.Scope != RoleScope.System, ct))
            throw new TenantInvitationTargetException();

        var slug = await db.Tenants.AsNoTracking().Where(t =>
                t.Id == tenantId && t.IsActive).Select(t => t.Slug)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(slug) || !Slug.IsMatch(slug))
            throw new TenantInvitationTargetException();
        return (target.Email, slug);
    }

    /// <summary>
    /// 1. Commit hash-only token issuance and audit;
    /// 2. Submit to configured SMTP (outside SQL transaction);
    /// 3. Mark submitted + audit in a second transaction.
    /// A crash between steps leaves a NON-REDEEMABLE pending token. Failed
    /// dispatch is revoked, never reported as an invitation sent.
    /// </summary>
    public static async Task<SubmissionReceipt> InviteAsync(
        ITenantContext current, IdentityDbContext db,
        ITenantInvitationSender sender, Guid memberId,
        CancellationToken ct = default)
    {
        if (!await CanInviteAsync(current, db, ct))
            throw new UnauthorizedAccessException("Tenant invite is not authorized.");
        if (!sender.IsConfigured)
            throw new TenantInvitationDeliveryException();

        var target = await TargetAsync(current, db, memberId, ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SHA256.HashData(Convert.FromHexString(token));
        var now = DateTime.UtcNow;
        var expiry = now.AddMinutes(30);
        var inviteId = Guid.NewGuid();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanInviteAsync(current, db, ct))
                throw new UnauthorizedAccessException("Tenant invite permission revoked.");
            // Recheck target after acquiring the serializable write boundary.
            await TargetAsync(current, db, memberId, ct);

            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberInvitations]
                SET [RevokedAtUtc] = {now}
                WHERE [TenantId] = {tenantId} AND [TargetUserId] = {memberId}
                    AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberInvitations]
                    ([Id],[TenantId],[TargetUserId],[IssuedByUserId],
                     [TokenHash],[CreatedAtUtc],[ExpiresAtUtc])
                VALUES ({inviteId},{tenantId},{memberId},{actor},
                        {tokenHash},{now},{expiry})
                """, ct);
            await AuditAsync(db, tenantId, actor, memberId, "invite_issued", ct);
            await tx.CommitAsync(ct);
        });

        try
        {
            // Token goes to the member's registered email ONLY, not the Owner.
            await sender.SendAsync(target.Email, target.TenantSlug, token, ct);
        }
        catch (Exception)
        {
            // The pending token is non-redeemable even if revocation fails.
            await UpdateDeliveryAsync(db, inviteId, submitted: false,
                CancellationToken.None);
            throw new TenantInvitationDeliveryException();
        }

        // This means accepted by configured mail transport, NOT delivered into
        // recipient's inbox. If persistence fails, activation stays disabled.
        await UpdateDeliveryAsync(db, inviteId, submitted: true, ct);
        return new SubmissionReceipt(expiry, "submitted");
    }

    private static async Task UpdateDeliveryAsync(
        IdentityDbContext db, Guid inviteId, bool submitted,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;
            var changed = submitted
                ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [dbo].[TenantMemberInvitations]
                    SET [SubmittedAtUtc] = {now}
                    WHERE [Id] = {inviteId} AND [SubmittedAtUtc] IS NULL
                      AND [RevokedAtUtc] IS NULL AND [UsedAtUtc] IS NULL
                      AND [ExpiresAtUtc] > {now}
                    """, ct)
                : await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [dbo].[TenantMemberInvitations]
                    SET [RevokedAtUtc] = {now}
                    WHERE [Id] = {inviteId} AND [SubmittedAtUtc] IS NULL
                      AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                    """, ct);
            if (changed != 1) throw new TenantInvitationDeliveryException();
            var rows = await db.Database.SqlQueryRaw<InvitationRow>(
                """
                SELECT [Id],[TenantId],[TargetUserId],[ExpiresAtUtc],
                       [SubmittedAtUtc],[UsedAtUtc],[RevokedAtUtc]
                FROM [dbo].[TenantMemberInvitations] WHERE [Id] = {0}
                """, inviteId).ToListAsync(ct);
            if (rows.Count != 1) throw new TenantInvitationDeliveryException();
            // System-generated event actor is the original issuing Owner, not
            // the (possibly anonymous) delivery retry worker.
            var actorIds = await db.Database.SqlQueryRaw<Guid>(
                "SELECT [IssuedByUserId] AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE [Id] = {0}",
                inviteId).ToListAsync(ct);
            if (actorIds.Count != 1) throw new TenantInvitationDeliveryException();
            await AuditAsync(db, rows[0].TenantId, actorIds[0],
                rows[0].TargetUserId, submitted ? "invite_submitted" : "invite_failed", ct);
            await tx.CommitAsync(ct);
        });
    }

    private static void ValidateActivation(ActivateRequest? request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.TenantSlug) ||
            !Slug.IsMatch(request.TenantSlug) ||
            request.Token is not { Length: 64 } ||
            !Regex.IsMatch(request.Token, @"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant) ||
            request.Password is not { Length: >= 12 and <= 128 } password ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)) ||
            password.Any(char.IsControl))
            throw new TenantInvitationValidationException();
    }

    /// <summary>
    /// Uses only an explicitly submitted 256-bit token: no ambient auth,
    /// cookie, role, Owner request or client-selected TenantId can activate.
    /// A serializable SQL transaction consumes token exactly once while
    /// changing password, active state, stamps and append-only audit.
    /// </summary>
    public static async Task ActivateAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        ActivateRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer()) throw new TenantInvitationValidationException();
        ValidateActivation(request);
        var hash = SHA256.HashData(Convert.FromHexString(request!.Token!));
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;

            // UPDLOCK + HOLDLOCK serializes two concurrent redemptions on
            // the unique hash index; the conditional UPDATE is the final CAS.
            var found = await db.Database.SqlQueryRaw<InvitationRow>(
                """
                SELECT [Id],[TenantId],[TargetUserId],[ExpiresAtUtc],
                       [SubmittedAtUtc],[UsedAtUtc],[RevokedAtUtc]
                FROM [dbo].[TenantMemberInvitations] WITH (UPDLOCK, HOLDLOCK)
                WHERE [TokenHash] = {0}
                """, hash).ToListAsync(ct);
            if (found.Count != 1 || found[0].SubmittedAtUtc is null ||
                found[0].UsedAtUtc is not null || found[0].RevokedAtUtc is not null ||
                found[0].ExpiresAtUtc <= now)
                throw new TenantInvitationValidationException();

            var invite = found[0];
            var tenantActive = await db.Tenants.AsNoTracking().AnyAsync(t =>
                t.Id == invite.TenantId && t.IsActive &&
                t.Slug == request.TenantSlug, ct);
            if (!tenantActive) throw new TenantInvitationValidationException();

            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.Id == invite.TargetUserId && u.TenantId == invite.TenantId &&
                u.UserType == UserType.Tenant && !u.IsActive && !u.EmailConfirmed, ct);
            if (user is null ||
                await db.UserRoles.AsNoTracking().AnyAsync(ur =>
                    ur.TenantId == invite.TenantId && ur.UserId == user.Id &&
                    ur.Role.TenantId == invite.TenantId &&
                    ur.Role.NormalizedName == "OWNER" &&
                    ur.Role.Scope != RoleScope.System, ct))
                throw new TenantInvitationValidationException();

            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            user.EmailConfirmed = true;
            user.IsActive = true;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.IsLockedOut = false;
            user.LockoutEndUtc = null;
            user.AccessFailedCount = 0;

            await db.SaveChangesAsync(ct);
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantMemberInvitations]
                SET [UsedAtUtc] = {now}
                WHERE [Id] = {invite.Id} AND [SubmittedAtUtc] IS NOT NULL
                  AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL
                  AND [ExpiresAtUtc] > {now}
                """, ct);
            if (affected != 1) throw new TenantInvitationValidationException();

            // Activation has no authenticated actor. The target is the
            // initiating identity, with action name distinguishing it.
            await AuditAsync(db, invite.TenantId, user.Id, user.Id,
                "invite_activated", ct);
            await tx.CommitAsync(ct);
        });
    }

    private static Task AuditAsync(IdentityDbContext db, Guid tenantId,
        Guid actor, Guid target, string action, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                ([Id],[TenantId],[ActorUserId],[TargetUserId],
                 [Action],[OccurredAtUtc])
            VALUES ({Guid.NewGuid()},{tenantId},{actor},{target},
                    {action},SYSUTCDATETIME())
            """, ct);
}
