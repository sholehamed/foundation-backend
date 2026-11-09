using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

public sealed class CurrentUserNavigationTests
{
    private static readonly Guid Master = DefaultSeedData.MasterTenant.Id;

    [Fact]
    public async Task System_navigation_uses_all_roles_groups_and_direct_denials()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(Master, "master");
        var user = f.User(null, UserType.System);
        var category = f.Category("Administration");
        var firstPermission = f.Permission("USERS.READ");
        var secondPermission = f.Permission("ROLES.READ");
        var deniedPermission = f.Permission("SECRETS.READ");

        var a = f.Role(Master, "reader");
        var b = f.Role(Master, "editor");
        f.AssignRole(user, a, Master);
        f.AssignRole(user, b, Master);
        f.Grant(a, firstPermission, Master);
        f.Grant(b, secondPermission, Master);
        var groupRole = f.Role(Master, "group-role");
        f.Grant(groupRole, deniedPermission, Master);
        f.GroupGrant(user, groupRole, Master);
        f.Menu(category, "Users", "/system/users", firstPermission.Id, order: 2);
        f.Menu(category, "Roles", "/system/roles", secondPermission.Id, order: 1);
        f.Menu(category, "Secrets", "/system/secret", deniedPermission.Id, order: 3);
        f.Direct(user, deniedPermission, Master, false);
        f.Save();

        var result = await new CurrentUserNavigationService(f.Db)
            .GetAsync(f.Principal(user, "system"));
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(new[] { "Roles", "Users" },
            result[0].Items.Select(x => x.Title).ToArray());

        // A denied group permission becomes accessible if direct deny is
        // removed; the snapshot token has not changed in this test.
        f.Db.UserPermissions.RemoveRange(
            f.Db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToList());
        f.Save();
        // Verify the fixture still exposes an inherited group grant in the
        // established single-permission evaluator after direct deny removal.
        // The fixture's tenant query filter is empty by default: ensure the
        // direct deny really was deleted before asserting inherited group grants.
        Assert.Empty(f.Db.UserPermissions.IgnoreQueryFilters(["tenant"]).ToList());
        Assert.True(await new EffectivePermissionEvaluator(f.Db).HasPermissionAsync(
            user.Id, Master, UserType.System, "SECRETS.READ"));
        var after = await new CurrentUserNavigationService(f.Db)
            .GetAsync(f.Principal(user, "system"));
        Assert.Equal(new[] { "Roles", "Users", "Secrets" },
            after![0].Items.Select(x => x.Title).ToArray());
    }

    [Fact]
    public async Task Tenant_navigation_rejects_system_routes_foreign_scope_and_unsafe_urls()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(Master, "master");
        var tenant = f.Tenant(Guid.NewGuid(), "own");
        var foreign = f.Tenant(Guid.NewGuid(), "foreign");
        var user = f.User(tenant.Id, UserType.Tenant);
        var category = f.Category("Workspace");
        var permission = f.Permission("DASHBOARD.VIEW");
        var role = f.Role(foreign.Id, "foreign-role");
        f.AssignRole(user, role, foreign.Id);
        f.Grant(role, permission, foreign.Id);
        f.Menu(category, "Foreign grant", "/reports", permission.Id);
        f.Menu(category, "Home", "/dashboard", null, order: 1);
        f.Menu(category, "System", "/system/users", null);
        f.Menu(category, "Javascript", "javascript:alert(1)", null);
        f.Menu(category, "Bad external", null, null, externalUrl: "javascript:alert(1)");
        f.Menu(category, "Docs", null, null, order: 2, externalUrl: "https://example.test/docs");
        f.Save();

        var service = new CurrentUserNavigationService(f.Db);
        var result = await service.GetAsync(f.Principal(user, "tenant", tenant));
        Assert.NotNull(result);
        Assert.Equal(new[] { "Home", "Docs" },
            result[0].Items.Select(x => x.Title).ToArray());

        Assert.Null(await service.GetAsync(f.Principal(user, "tenant", foreign)));
        Assert.Null(await service.GetAsync(f.Principal(user, "system")));
        Assert.Null(await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Tenant_workspace_navigation_requires_live_page_permissions_even_when_menu_has_none()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(Master, "master");
        var tenant = f.Tenant(Guid.NewGuid(), "own");
        var foreign = f.Tenant(Guid.NewGuid(), "foreign");
        var user = f.User(tenant.Id, UserType.Tenant);
        var category = f.Category("Tenant workspace");

        // An incorrectly configured Menu.PermissionId=null must NOT turn a
        // privileged Tenant PAGE into a public link in the dynamic sidebar.
        f.Menu(category, "Home", "/dashboard", null, order: 1);
        f.Menu(category, "Business", "/dashboard/business", null, order: 2);
        var parent = f.Menu(category, "Administration", null, null, order: 3);
        f.Menu(category, "Members", "/dashboard/members", null, parent.Id);
        f.Menu(category, "Roles", "/dashboard/roles", null, parent.Id);
        f.Menu(category, "Groups", "/dashboard/groups", null, parent.Id);
        f.Menu(category, "Unknown internal page", "/dashboard/secret", null);
        f.Menu(category, "Foreign tenant escape", "/system/admins", null);

        var members = f.Permission(
            Modules.System.Identity.Web.Endpoints.TenantWorkspacePermissionCatalog.MembersRead);
        var roles = f.Permission(
            Modules.System.Identity.Web.Endpoints.TenantWorkspacePermissionCatalog.RolesRead);
        var groupPermission = f.Permission(
            Modules.System.Identity.Web.Endpoints.TenantWorkspacePermissionCatalog.GroupsRead);

        var local = f.Role(tenant.Id, "operator");
        local.Scope = RoleScope.Tenant;
        f.AssignRole(user, local, tenant.Id);
        f.Grant(local, members, tenant.Id);

        // This is a malformed System-scope Role under the same Tenant.
        // The ordinary navigation projector must not accept its role grant.
        var wrongScope = f.Role(tenant.Id, "system-role");
        wrongScope.Scope = RoleScope.System;
        f.AssignRole(user, wrongScope, tenant.Id);
        f.Grant(wrongScope, groupPermission, tenant.Id);

        // A foreign Tenant role cannot confer local navigation authority.
        var foreignRole = f.Role(foreign.Id, "foreign-role");
        foreignRole.Scope = RoleScope.Tenant;
        f.AssignRole(user, foreignRole, foreign.Id);
        f.Grant(foreignRole, roles, foreign.Id);
        f.Save();

        f.Current.InitializeTenant(user.Id, tenant.Id, [tenant.Id], [tenant.Id], tenant.Id);
        var principal = f.Principal(user, "tenant", tenant);
        var allowed = await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, principal);
        Assert.NotNull(allowed);
        var top = Assert.Single(allowed);
        Assert.Equal(new[] { "Home", "Business", "Administration" },
            top.Items.Select(x => x.Title));
        Assert.Equal("Members", Assert.Single(top.Items[2].Childs).Title);

        // With owned-catalog mode selected on a provider without the reviewed
        // SQL schema, the old global menu MUST NOT reappear as a fallback.
        var noSqlOwned = await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, principal, ownedCatalogEnabled: true);
        Assert.Empty(noSqlOwned!);

        // The HTTP endpoint is a scoped read-only view, not permission grant.
        var http = new DefaultHttpContext { User = principal };
        var httpResult = await TenantWorkspace.GetTenantNavigation(
            f.Current, f.Db, http, new ConfigurationBuilder().Build());
        Assert.IsType<Ok<List<CurrentNavigationCategory>>>(httpResult);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());

        var ownedConfig = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["Identity:TenantNavigation:OwnedCatalogEnabled"] = "true"
            }).Build();
        var featureOnResult = await TenantWorkspace.GetTenantNavigation(
            f.Current, f.Db, http, ownedConfig);
        Assert.Empty(Assert.IsType<Ok<List<CurrentNavigationCategory>>>(
            featureOnResult).Value!);

        // A live direct user DENY wins over the role. Parent containers with
        // zero children disappear rather than leaking a hidden section name.
        f.Direct(user, members, tenant.Id, false);
        f.Save();
        var denied = await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, principal);
        Assert.Equal(new[] { "Home", "Business" },
            Assert.Single(denied!).Items.Select(x => x.Title));

        // Grant by a live local GroupRole is recognized through the SAME
        // evaluator. Deactivating its Group removes navigation immediately.
        var groupRole = f.Role(tenant.Id, "group-reader");
        groupRole.Scope = RoleScope.Tenant;
        f.Grant(groupRole, roles, tenant.Id);
        f.GroupGrant(user, groupRole, tenant.Id);
        f.Save();
        var withGroup = await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, principal);
        Assert.Equal("Roles",
            Assert.Single(Assert.Single(withGroup!).Items[2].Childs).Title);
        var group = await f.Db.Groups.SingleAsync(x => x.TenantId == tenant.Id);
        group.IsActive = false;
        f.Save();
        var afterRevoke = await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, principal);
        Assert.Equal(2, Assert.Single(afterRevoke!).Items.Count);

        // Subject/Tenant and live context must agree; no client-supplied
        // TenantId or RoleId can select a new navigation scope.
        Assert.Null(await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, f.Principal(user, "tenant", foreign)));
        Assert.Null(await TenantWorkspaceNavigationService.GetAsync(
            f.Current, f.Db, f.Principal(user, "system")));
        var crossTenant = new TenantContext();
        crossTenant.InitializeTenant(user.Id, foreign.Id,
            [foreign.Id], [foreign.Id], foreign.Id);
        Assert.Null(await TenantWorkspaceNavigationService.GetAsync(
            crossTenant, f.Db, principal));
        var deniedHttp = await TenantWorkspace.GetTenantNavigation(
            crossTenant, f.Db, http, new ConfigurationBuilder().Build());
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeHttpResult>(deniedHttp).StatusCode);
    }

    [Fact]
    public async Task Disabled_ancestor_category_and_bad_parent_graph_fail_closed()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(Master, "master");
        var user = f.User(null, UserType.System);
        var category = f.Category("Administration");
        var permission = f.Permission("MENU.READ");
        var parent = f.Menu(category, "Parent", null, permission.Id);
        f.Menu(category, "Child", "/system/users", null, parent.Id);
        f.Menu(category, "Orphan", "/system/orphan", null, Guid.NewGuid());
        var cyclicA = f.Menu(category, "Cycle A", "/system/a", null);
        var cyclicB = f.Menu(category, "Cycle B", "/system/b", null, cyclicA.Id);
        cyclicA.ParentId = cyclicB.Id;
        f.Save();

        var service = new CurrentUserNavigationService(f.Db);
        var principal = f.Principal(user, "system");

        // A denied parent must not leak child navigation.
        Assert.Empty((await service.GetAsync(principal))!);
        f.Direct(user, permission, Master, true);
        f.Save();
        var allowed = await service.GetAsync(principal);
        Assert.Equal("Parent", Assert.Single(Assert.Single(allowed!).Items).Title);
        Assert.Equal("Child", Assert.Single(allowed[0].Items[0].Childs).Title);

        category.IsActive = false;
        f.Save();
        Assert.Empty((await service.GetAsync(principal))!);
    }

    [Fact]
    public async Task Missing_active_identity_and_empty_catalog_do_not_return_admin_fallback()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(Master, "master");
        var user = f.User(null, UserType.System);
        f.Save();
        var service = new CurrentUserNavigationService(f.Db);
        var principal = f.Principal(user, "system");
        Assert.Empty((await service.GetAsync(principal))!);

        user.IsActive = false;
        f.Save();
        Assert.Null(await service.GetAsync(principal));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityDbContext Db { get; }
        public TenantContext Current { get; }
        private Fixture(IdentityDbContext db, TenantContext current)
        {
            Db = db;
            Current = current;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
            var current = new TenantContext();
            var db = new IdentityDbContext(options, current);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(db, current);
        }

        public Tenant Tenant(Guid id, string slug)
        {
            var entity = new Tenant(id, null, slug, slug, slug + ".example.test")
            { RowVersion = [1], IsActive = true };
            Db.Tenants.Add(entity);
            return entity;
        }

        public User User(Guid? tenantId, UserType type)
        {
            var id = Guid.NewGuid();
            var entity = new User(id, tenantId, "nav-" + id.ToString("N"),
                id.ToString("N") + "@example.test", "Nav", "User", type)
            {
                RowVersion = [1], IsActive = true, SecurityStamp = "stamp",
                PasswordHash = "test-only",
                NormalizedUserName = id.ToString("N").ToUpperInvariant(),
                NormalizedEmail = id.ToString("N").ToUpperInvariant() + "@EXAMPLE.TEST"
            };
            Db.Users.Add(entity);
            return entity;
        }

        public MenuCategory Category(string title)
        {
            var entity = new MenuCategory(Guid.NewGuid())
            { RowVersion = [1], Title = title, IsActive = true };
            Db.MenuCategories.Add(entity);
            return entity;
        }

        public Permission Permission(string code)
        {
            var entity = new Permission(Guid.NewGuid(), code, null, code, null)
            { RowVersion = [1], IsActive = true };
            Db.Permissions.Add(entity);
            return entity;
        }

        public Role Role(Guid tenantId, string title)
        {
            var entity = new Role(tenantId, title, null)
            { RowVersion = [1], IsActive = true };
            Db.Roles.Add(entity);
            return entity;
        }

        public void AssignRole(User user, Role role, Guid scope) =>
            Db.UserRoles.Add(new UserRole(scope, user.Id, role.Id) { RowVersion = [1] });

        public void Grant(Role role, Permission permission, Guid scope) =>
            Db.RolePermissions.Add(new RolePermission(scope, role.Id, permission.Id) { RowVersion = [1] });

        public void GroupGrant(User user, Role role, Guid scope)
        {
            var group = new Group(Guid.NewGuid())
            { RowVersion = [1], TenantId = scope, Name = "Navigation", NormalizedName = "NAVIGATION" };
            Db.Groups.Add(group);
            Db.UserGroups.Add(new UserGroup(Guid.NewGuid())
            { RowVersion = [1], TenantId = scope, UserId = user.Id, GroupId = group.Id });
            Db.GroupRoles.Add(new GroupRole(Guid.NewGuid())
            { RowVersion = [1], TenantId = scope, GroupId = group.Id, RoleId = role.Id });
        }

        public UserPermission Direct(User user, Permission permission, Guid scope, bool granted)
        {
            var entity = new UserPermission(Guid.NewGuid())
            {
                RowVersion = [1], TenantId = scope, UserId = user.Id,
                PermissionId = permission.Id, IsGranted = granted
            };
            Db.UserPermissions.Add(entity);
            return entity;
        }

        public Menu Menu(MenuCategory category, string title, string? route,
            Guid? permissionId, Guid? parentId = null, short order = 0,
            string? externalUrl = null)
        {
            var entity = new Menu(Guid.NewGuid())
            {
                RowVersion = [1], CategoryId = category.Id,
                ParentId = parentId, Title = title, Icon = "folder",
                Route = route, ExternalUrl = externalUrl, Order = order,
                PermissionId = permissionId, IsActive = true
            };
            Db.Menus.Add(entity);
            return entity;
        }

        public ClaimsPrincipal Principal(User user, string type, Tenant? tenant = null)
        {
            var claims = new List<Claim>
            {
                new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new("account_type", type)
            };
            if (tenant is not null)
            {
                claims.Add(new Claim("tenant_id", tenant.Id.ToString()));
                claims.Add(new Claim("tenant", tenant.Slug));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        public void Save() => Db.SaveChanges();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
