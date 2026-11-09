using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantMemberCreateValidationException(string message) : Exception(message);
public sealed class TenantMemberCreateConflictException(string message) : Exception(message);

/// <summary>
/// FR-13B-1: create a local, NON-LOGIN Tenant staff record. No invitation is
/// issued, no password is disclosed and no UserRole/group/permission is added.
/// A separate, reviewed activation flow is required before the account can log in.
/// </summary>
public static class TenantPendingMemberWriter
{
    public sealed record CreateRequest(
        string? UserName, string? Email, string? FirstName, string? LastName);

    public sealed record MemberView(
        Guid Id, string UserName, string Email, string? FirstName,
        string? LastName, bool IsActive);

    private static readonly Regex SafeUserName = new(
        @"\A[a-zA-Z0-9][a-zA-Z0-9._+\-]{2,99}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<bool> CanCreateAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;

        // A new identity-creation capability must never fall back to merely
        // owning a Tenant if the reviewed ACTION catalog has not been applied.
        var registered = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.MembersCreate &&
                p.IsActive && p.ResourceId != null && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                // A permission linked to any additional/foreign UI must not
                // authorize this new member-creation capability.
                db.PermissionUiResources.Count(l => l.PermissionId == p.Id) == 1 &&
                db.PermissionUiResources.Any(l =>
                    l.PermissionId == p.Id && l.UiResource != null &&
                    l.UiResource.IsActive && l.UiResource.ResourceId == p.ResourceId &&
                    l.UiResource.Key == "tenant.workspace.members.create" &&
                    l.UiResource.Type == UiResourceType.ACTION &&
                    l.UiResource.Description == null))
            .Select(p => p.Id).ToListAsync(ct);
        if (registered.Count != 1) return false;

        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersCreate, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    private static (string UserName, string NormalizedUserName,
        string Email, string NormalizedEmail, string FirstName, string LastName)
        Validate(CreateRequest? input)
    {
        if (input is null)
            throw new TenantMemberCreateValidationException("Member input is required.");

        var userName = input.UserName?.Trim() ?? "";
        var email = input.Email?.Trim() ?? "";
        var firstName = input.FirstName?.Trim() ?? "";
        var lastName = input.LastName?.Trim() ?? "";

        if (!SafeUserName.IsMatch(userName))
            throw new TenantMemberCreateValidationException(
                "Username must be 3-100 ASCII letters, digits, dot, underscore, plus or dash.");
        if (email.Length is < 5 or > 256 || email.Any(char.IsControl) ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new TenantMemberCreateValidationException("Invalid member email.");
        if (firstName.Length is < 1 or > 80 || lastName.Length is < 1 or > 80 ||
            firstName.Any(char.IsControl) || lastName.Any(char.IsControl))
            throw new TenantMemberCreateValidationException(
                "First and last name are required, each at most 80 characters.");

        return (userName, userName.ToUpperInvariant(),
            email, email.ToUpperInvariant(), firstName, lastName);
    }

    public sealed record CreateWithInitialPasswordResult(
        MemberView Member, string TemporaryPassword, DateTime ExpiresAtUtc);

    /// <summary>
    /// Atomic Owner onboarding: local inactive User + one-time initial password
    /// + both immutable audit records commit in ONE SQL transaction. This is
    /// opt-in, in addition to unchanged CreateAsync and email invitation.
    /// Caller verifies the separately default-OFF feature toggle and HTTPS.
    /// </summary>
    public static async Task<CreateWithInitialPasswordResult> CreateWithInitialPasswordAsync(
        ITenantContext current, IdentityDbContext db, IPasswordHasher<User> hasher,
        CreateRequest? input, CancellationToken ct = default)
    {
        if (!await CanCreateAsync(current, db, ct) ||
            !await TenantFirstLoginWriter.CanIssueAsync(current, db, ct))
            throw new UnauthorizedAccessException("Creating an initial credential is not authorized.");
        var fields = Validate(input);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanCreateAsync(current, db, ct) ||
                !await TenantFirstLoginWriter.CanIssueAsync(current, db, ct))
                throw new UnauthorizedAccessException("Tenant onboarding authorization was revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            if (await db.Users.AsNoTracking().AnyAsync(u =>
                    u.TenantId == tenantId && u.UserType == UserType.Tenant &&
                    (u.NormalizedUserName == fields.NormalizedUserName ||
                     u.NormalizedEmail == fields.NormalizedEmail), ct))
                throw new TenantMemberCreateConflictException(
                    "A member with this username or email already exists in this Tenant.");
            if (await db.Users.AsNoTracking().CountAsync(u =>
                    u.TenantId == tenantId && u.UserType == UserType.Tenant, ct) >= 10000)
                throw new TenantMemberCreateConflictException("Tenant member limit reached.");

            var member = new User(tenantId, fields.UserName, fields.Email,
                fields.FirstName, fields.LastName, UserType.Tenant, isActive: false)
            {
                NormalizedUserName = fields.NormalizedUserName,
                NormalizedEmail = fields.NormalizedEmail,
                EmailConfirmed = false,
                IsActive = false,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            // No temporary password can ever be mistaken for the real
            // login password: a separate random unknown hash stays on User.
            member.PasswordHash = hasher.HashPassword(member,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            db.Users.Add(member);
            await db.SaveChangesAsync(ct);

            var temporaryPassword = TenantFirstLoginWriter.GenerateTemporaryPassword();
            var hash = hasher.HashPassword(member, temporaryPassword);
            var now = DateTime.UtcNow;
            var expiry = now.AddMinutes(60);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberTemporaryCredentials]
                    ([Id],[TenantId],[TargetUserId],[IssuedByUserId],
                     [PasswordHash],[CreatedAtUtc],[ExpiresAtUtc],[FailedAttempts])
                VALUES ({Guid.NewGuid()},{tenantId},{member.Id},{actor},
                    {hash},{now},{expiry},{0})
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                    ([Id],[TenantId],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
                VALUES ({Guid.NewGuid()},{tenantId},{actor},{member.Id},
                    {"create_inactive"},SYSUTCDATETIME())
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                    ([Id],[TenantId],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
                VALUES ({Guid.NewGuid()},{tenantId},{actor},{member.Id},
                    {"temp_issued"},SYSUTCDATETIME())
                """, ct);
            await tx.CommitAsync(ct);
            return new CreateWithInitialPasswordResult(
                new MemberView(member.Id, member.UserName, member.Email,
                    member.FirstName, member.LastName, member.IsActive),
                temporaryPassword, expiry);
        });
    }

    public static async Task<MemberView> CreateAsync(
        ITenantContext current, IdentityDbContext db, IPasswordHasher<User> hasher,
        CreateRequest? input, CancellationToken ct = default)
    {
        if (!await CanCreateAsync(current, db, ct))
            throw new UnauthorizedAccessException("Tenant member creation is not authorized.");
        var fields = Validate(input);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            // Recheck all live actor, Owner, catalog and tenant scope conditions
            // within the same serializable transaction as the audited write.
            if (!await CanCreateAsync(current, db, ct))
                throw new UnauthorizedAccessException("Tenant member creation permission revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            if (await db.Users.AsNoTracking().AnyAsync(u =>
                    u.TenantId == tenantId && u.UserType == UserType.Tenant &&
                    (u.NormalizedUserName == fields.NormalizedUserName ||
                     u.NormalizedEmail == fields.NormalizedEmail), ct))
                throw new TenantMemberCreateConflictException(
                    "A member with this username or email already exists in this Tenant.");

            // A bounded first rollout: do not let the new write path create
            // unbounded accounts within a single Tenant even with a valid Owner.
            if (await db.Users.AsNoTracking().CountAsync(u =>
                    u.TenantId == tenantId && u.UserType == UserType.Tenant, ct) >= 10000)
                throw new TenantMemberCreateConflictException("Tenant member limit reached.");

            var member = new User(tenantId, fields.UserName, fields.Email,
                fields.FirstName, fields.LastName, UserType.Tenant, isActive: false)
            {
                NormalizedUserName = fields.NormalizedUserName,
                NormalizedEmail = fields.NormalizedEmail,
                EmailConfirmed = false,
                IsActive = false,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };

            // Non-disclosed, cryptographically random value; unlike an empty or
            // placeholder password, it cannot be used as a login credential.
            // The account stays inactive even if this hash is compromised.
            var unknownPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            member.PasswordHash = hasher.HashPassword(member, unknownPassword);
            db.Users.Add(member);
            await db.SaveChangesAsync(ct);

            // Independent, append-only SQL ledger. If the audit insert fails,
            // rollback also removes the new User.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                    ([Id], [TenantId], [ActorUserId], [TargetUserId], [Action], [OccurredAtUtc])
                VALUES ({Guid.NewGuid()}, {tenantId}, {actor}, {member.Id},
                    {"create_inactive"}, SYSUTCDATETIME())
                """, ct);
            await tx.CommitAsync(ct);

            return new MemberView(member.Id, member.UserName, member.Email,
                member.FirstName, member.LastName, member.IsActive);
        });
    }
}
