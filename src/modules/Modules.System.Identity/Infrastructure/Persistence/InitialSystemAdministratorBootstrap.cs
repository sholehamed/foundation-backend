using System.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Secure one-time operator-configured first System administrator credential.
/// InitialAccessBootstrap creates all roles/permissions/menus; this associates
/// an actual login identity ONLY if the database has never had any System user.
/// Not a recurring role reconciliation and never a default password seeder.
/// </summary>
public static class InitialSystemAdministratorBootstrap
{
    private const string Prefix = "Identity:Bootstrap:InitialSystemAdmin:";
    private static readonly Regex UserNamePattern = new(
        @"\A[a-zA-Z0-9][a-zA-Z0-9._+\-]{2,99}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<bool> CreateOnceAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        IConfiguration configuration, CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>(Prefix + "Enabled"))
            return false;

        // A wrongly configured secret must abort rather than silently make
        // an account no human can access. The error never contains secrets.
        var rawName = configuration[Prefix + "UserName"]?.Trim();
        var rawEmail = configuration[Prefix + "Email"]?.Trim();
        var password = configuration[Prefix + "Password"];
        if (rawName is null || !UserNamePattern.IsMatch(rawName) ||
            rawEmail is not { Length: >= 5 and <= 256 } ||
            !MailAddress.TryCreate(rawEmail, out var address) ||
            !string.Equals(address.Address, rawEmail, StringComparison.OrdinalIgnoreCase) ||
            password is not { Length: >= 15 and <= 128 } ||
            password.Any(char.IsControl) ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit) ||
            !password.Any(c => !char.IsLetterOrDigit(c)) ||
            password.Contains(rawName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Initial System Admin bootstrap requires a valid unique username/email and a strong explicit password (15+ characters, letters/digits/symbols).");

        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException(
                "Initial System Admin bootstrap is supported only with migrated SQL Server.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            // Any existing System identity, even a disabled one, means a human
            // has already provisioned the System namespace. Never reinstate a
            // revoked/deactivated administrator on app restart.
            if (await db.Users.AsNoTracking().AnyAsync(x =>
                    x.UserType == UserType.System, ct))
                return false;

            // No legacy Tenant or account may exist: only the initial Master
            // scope with a canonical code-owned SuperAdmin Role is eligible.
            var master = DefaultSeedData.MasterTenantId;
            if (await db.Tenants.AsNoTracking().CountAsync(ct) != 1 ||
                !await db.Tenants.AsNoTracking().AnyAsync(x =>
                    x.Id == master && x.IsActive, ct) ||
                await db.Users.AsNoTracking().AnyAsync(ct) ||
                await db.UserRoles.IgnoreQueryFilters(["tenant"])
                    .AsNoTracking().AnyAsync(ct) ||
                await db.Roles.IgnoreQueryFilters(["tenant"])
                    .AsNoTracking().CountAsync(ct) != 1)
                throw new InvalidOperationException(
                    "Initial System Admin bootstrap is allowed only on a new Master-only installation.");

            var role = await db.Roles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == master && x.NormalizedName == "SUPERADMIN" &&
                    x.Scope == RoleScope.System && x.IsActive, ct);
            if (role is null)
                throw new InvalidOperationException(
                    "Initial System SuperAdmin role was not registered.");

            var systemCodes = FeatureRegistryDefinitions.Features
                .SelectMany(x => x.PermissionCodes)
                .ToHashSet(StringComparer.Ordinal);
            var grants = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x =>
                    x.TenantId == master && x.RoleId == role.Id &&
                    x.Permission != null && x.Permission.IsActive &&
                    x.Permission.Resource != null &&
                    x.Permission.Resource.IsActive &&
                    x.Permission.Resource.Module != null &&
                    x.Permission.Resource.Module.Code == "system" &&
                    x.Permission.Resource.Module.IsActive)
                .Select(x => x.Permission!.Code).ToListAsync(ct);
            if (grants.Count != systemCodes.Count ||
                !grants.ToHashSet(StringComparer.Ordinal).SetEquals(systemCodes))
                throw new InvalidOperationException(
                    "Initial SuperAdmin must have all and only code-owned System permissions.");

            var roots = FeatureRegistryDefinitions.Features.Select(x => x.Route)
                .ToHashSet(StringComparer.Ordinal);
            var menus = await db.Menus.AsNoTracking()
                .Where(x => x.IsActive && !x.IsHidden &&
                    x.Route != null && roots.Contains(x.Route))
                .Select(x => x.Route).ToListAsync(ct);
            if (menus.Count != roots.Count ||
                !menus.ToHashSet(StringComparer.Ordinal).SetEquals(roots))
                throw new InvalidOperationException(
                    "Initial System menus are incomplete; refusing to issue admin credentials.");

            var user = new User(null, rawName, rawEmail,
                "System", "Administrator", UserType.System)
            {
                NormalizedUserName = rawName.ToUpperInvariant(),
                NormalizedEmail = rawEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                IsLockedOut = false,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole(master, user.Id, role.Id));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
    }
}
