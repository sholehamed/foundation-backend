using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using System.Security.Claims;
using Xunit;

public sealed class IdentityAuthorizationDatabaseTests
{
    [Fact]
    public async Task Removing_system_admin_role_blocks_old_access_token_and_refresh()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection).Options;
        var tenant = new TenantContext();

        await using var db = new SqliteIdentityDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();

        var systemScope = Modules.System.Identity.Infrastructure.Persistence.DefaultSeedData.MasterTenant.Id;
        var user = new User(null, "admin", "admin@example.test",
            "System", "Admin", UserType.System)
        {
            NormalizedUserName = "ADMIN",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            SecurityStamp = "initial-stamp",
            PasswordHash = "test-only-not-used",
        };
        var role = new Role(systemScope, "superadmin", "Platform administrator");
        var assignment = new UserRole(systemScope, user.Id, role.Id);

        db.Users.Add(user);
        db.Roles.Add(role);
        db.UserRoles.Add(assignment);
        await db.SaveChangesAsync();

        var claims = new ClaimsPrincipal(new ClaimsIdentity([
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new("account_type", "system"),
            new("security_stamp", "initial-stamp"),
            new(OpenIddictConstants.Claims.Role, "superadmin")
        ], "test"));

        var refresh = new RefreshGrantValidator(db);
        Assert.True(await refresh.IsStillAuthorizedAsync(claims));

        var requirement = new ActiveSystemAdministratorRequirement();
        var handler = new ActiveSystemAdministratorHandler(db);
        var before = new AuthorizationHandlerContext([requirement], claims, null);
        await handler.HandleAsync(before);
        Assert.True(before.HasSucceeded);

        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync();

        Assert.False(await refresh.IsStillAuthorizedAsync(claims));
        var after = new AuthorizationHandlerContext([requirement], claims, null);
        await handler.HandleAsync(after);
        Assert.False(after.HasSucceeded);
    }

    [Fact]
    public async Task Disabled_account_must_not_refresh_even_if_token_snapshot_is_unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection).Options;

        await using var db = new SqliteIdentityDbContext(options, new TenantContext());
        await db.Database.EnsureCreatedAsync();

        var user = new User(null, "disabled", "disabled@example.test",
            "System", "User", UserType.System)
        {
            NormalizedUserName = "DISABLED",
            NormalizedEmail = "DISABLED@EXAMPLE.TEST",
            IsActive = false,
            SecurityStamp = "stamp",
            PasswordHash = "test-only-not-used",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new("account_type", "system"),
            new("security_stamp", "stamp"),
        ], "test"));

        Assert.False(await new RefreshGrantValidator(db).IsStillAuthorizedAsync(principal));
    }
    // SQLite does not implement SQL Server's auto-generated rowversion type.
    // Provide insertion-time byte values for all concurrency properties in
    // this isolated test model. SQL Server semantics need separate integration tests.
    private sealed class SqliteIdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        Modules.System.Identity.Application.Abstractions.ITenantContext tenantContext)
        : IdentityDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (entity.ClrType is null || entity.FindProperty("RowVersion") is null)
                    continue;

                modelBuilder.Entity(entity.ClrType)
                    .Property<byte[]>("RowVersion")
                    .HasColumnType("BLOB")
                    .HasDefaultValueSql("randomblob(8)");
            }
        }
    }
}
