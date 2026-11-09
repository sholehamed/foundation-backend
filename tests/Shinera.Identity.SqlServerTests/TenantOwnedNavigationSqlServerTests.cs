using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Real disposable SQL Server schema constraints, scoped read projection,
/// direct-deny revocation, and global catalog non-fallback for FR-14C-2.
/// </summary>
public sealed class TenantOwnedNavigationSqlServerTests
{
    [Fact]
    public async Task Owned_catalog_is_tenant_scoped_and_live_page_grants_are_authoritative()
    {
        await WithSqlAsync(async (db, current) =>
        {
            var alphaId = Guid.NewGuid();
            var betaId = Guid.NewGuid();
            var user = new User(alphaId, "owned-reader", "owned-reader@alpha.test",
                "Menu", "Reader", UserType.Tenant)
            {
                NormalizedUserName = "OWNED-READER",
                NormalizedEmail = "OWNED-READER@ALPHA.TEST",
                PasswordHash = "fixture-only", SecurityStamp = "fixture-stamp",
                IsActive = true
            };
            var alpha = new Tenant(alphaId, null, "Alpha", "owned-alpha", "alpha.test");
            var beta = new Tenant(betaId, null, "Beta", "owned-beta", "beta.test");
            var permission = new Permission(Guid.NewGuid(),
                "TENANT.MEMBERS.READ", null, "Read members", null);
            var role = new Role(alphaId, "member-reader", null)
                { Scope = RoleScope.Tenant, IsActive = true };
            db.AddRange(alpha, beta, user, permission, role,
                new UserRole(alphaId, user.Id, role.Id),
                new RolePermission(alphaId, role.Id, permission.Id));
            await db.SaveChangesAsync();

            var alphaCat = Guid.NewGuid();
            var betaCat = Guid.NewGuid();
            var ownHomeId = Guid.NewGuid();
            var ownMemberId = Guid.NewGuid();
            var foreignId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationCategories]
                    ([TenantId],[Id],[Title])
                VALUES ({alphaId},{alphaCat},{"Alpha menus"})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationCategories]
                    ([TenantId],[Id],[Title])
                VALUES ({betaId},{betaCat},{"Beta private menu"})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationItems]
                    ([TenantId],[Id],[CategoryId],[Title],[Route],[SortOrder])
                VALUES ({alphaId},{ownHomeId},{alphaCat},{"Alpha home"},{"/dashboard"},{(short)1})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationItems]
                    ([TenantId],[Id],[CategoryId],[Title],[Route],[SortOrder])
                VALUES ({alphaId},{ownMemberId},{alphaCat},{"Alpha members"},{"/dashboard/members"},{(short)2})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationItems]
                    ([TenantId],[Id],[CategoryId],[Title],[Route])
                VALUES ({betaId},{foreignId},{betaCat},{"Secret beta"},{"/dashboard/roles"})
                """);

            current.InitializeTenant(user.Id, alphaId, [alphaId], [alphaId], alphaId);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new Claim("account_type", "tenant"),
                new Claim("tenant_id", alphaId.ToString()),
                new Claim("tenant", alpha.Slug)
            ], "test"));

            var owned = await TenantWorkspaceNavigationService.GetAsync(
                current, db, principal, ownedCatalogEnabled: true);
            var category = Assert.Single(owned!);
            Assert.Equal("Alpha menus", category.Title);
            Assert.Equal(new[] { "Alpha home", "Alpha members" },
                category.Items.Select(x => x.Title));
            Assert.DoesNotContain("Secret beta", category.Items.Select(x => x.Title));

            db.UserPermissions.Add(new UserPermission(Guid.NewGuid())
            {
                TenantId = alphaId, UserId = user.Id, PermissionId = permission.Id,
                IsGranted = false
            });
            await db.SaveChangesAsync();
            var revoked = await TenantWorkspaceNavigationService.GetAsync(
                current, db, principal, ownedCatalogEnabled: true);
            Assert.Equal("Alpha home", Assert.Single(Assert.Single(revoked!).Items).Title);

            // A freshly created global menu MUST NOT be used to fill an empty
            // Tenant-owned catalog. Read model is isolated from global data.
            db.MenuCategories.Add(new MenuCategory(Guid.NewGuid())
                { Title = "Old global", IsActive = true });
            await db.SaveChangesAsync();
            var globalCategory = await db.MenuCategories.SingleAsync(
                c => c.Title == "Old global");
            db.Menus.Add(new Menu(Guid.NewGuid())
            {
                CategoryId = globalCategory.Id, Title = "Global home",
                Icon = "home", Route = "/dashboard", IsActive = true, IsHidden = false
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantNavigationCategories]
                SET [IsActive] = {false} WHERE [TenantId] = {alphaId}
                """);
            var emptyOwned = await TenantWorkspaceNavigationService.GetAsync(
                current, db, principal, ownedCatalogEnabled: true);
            Assert.Empty(emptyOwned!);

            var legacy = await TenantWorkspaceNavigationService.GetAsync(
                current, db, principal, ownedCatalogEnabled: false);
            Assert.Equal("Global home", Assert.Single(Assert.Single(legacy!).Items).Title);

            var forged = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new Claim("account_type", "tenant"),
                new Claim("tenant_id", betaId.ToString()),
                new Claim("tenant", beta.Slug)
            ], "test"));
            Assert.Null(await TenantWorkspaceNavigationService.GetAsync(
                current, db, forged, ownedCatalogEnabled: true));
        });
    }

    [Fact]
    public async Task Composite_foreign_keys_prevent_cross_tenant_or_cross_category_parent_leaks()
    {
        await WithSqlAsync(async (db, _) =>
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var catA = Guid.NewGuid();
            var catB = Guid.NewGuid();
            var parentA = Guid.NewGuid();
            db.Tenants.AddRange(
                new Tenant(tenantA, null, "A", "nav-a", "a.test"),
                new Tenant(tenantB, null, "B", "nav-b", "b.test"));
            await db.SaveChangesAsync();

            foreach (var (tenant, category, title) in new[] {
                (tenantA, catA, "A"), (tenantB, catB, "B") })
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantNavigationCategories]
                        ([TenantId],[Id],[Title])
                    VALUES ({tenant},{category},{title})
                    """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationItems]
                    ([TenantId],[Id],[CategoryId],[Title],[Route])
                VALUES ({tenantA},{parentA},{catA},{"Parent"},{"/dashboard"})
                """);

            // Category belongs to B, but request claims A: composite category
            // FK blocks insertion rather than relying on an application WHERE.
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantNavigationItems]
                        ([TenantId],[Id],[CategoryId],[Title],[Route])
                    VALUES ({tenantA},{Guid.NewGuid()},{catB},{"Forged"},{"/dashboard/business"})
                    """));
            // Parent belongs to A. Inserting the reference under B is blocked.
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantNavigationItems]
                        ([TenantId],[Id],[CategoryId],[ParentId],[Title],[Route])
                    VALUES ({tenantB},{Guid.NewGuid()},{catB},{parentA},{"Forged parent"},{"/dashboard/business"})
                    """));
            // Parent under another category of the same Tenant is also
            // invalid; nested menu graphs cannot cross category boundaries.
            var secondA = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationCategories]
                    ([TenantId],[Id],[Title])
                VALUES ({tenantA},{secondA},{"Another category"})
                """);
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantNavigationItems]
                        ([TenantId],[Id],[CategoryId],[ParentId],[Title],[Route])
                    VALUES ({tenantA},{Guid.NewGuid()},{secondA},{parentA},{"Wrong category"},{"/dashboard/business"})
                    """));
            // Duplicate active route in a tenant is not an ambiguous second
            // menu item. The unique filtered index prevents it.
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantNavigationItems]
                        ([TenantId],[Id],[CategoryId],[Title],[Route])
                    VALUES ({tenantA},{Guid.NewGuid()},{catA},{"Duplicate"},{"/dashboard"})
                    """));

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantNavigationItems] " +
                "WHERE [TenantId]={0}", tenantA).SingleAsync());
        });
    }

    private static async Task WithSqlAsync(
        Func<IdentityDbContext, TenantContext, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("SHINERA_SQLSERVER_TEST_CONNECTION required.");
        var cs = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "OwnedNavTest_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 30
        };
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var db = new IdentityDbContext(options, tenant);
        try
        {
            await db.Database.MigrateAsync();
            await action(db, tenant);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
