using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Features.Dashboard;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Xunit;

public sealed class SystemDashboardTests
{
    [Fact]
    public async Task Excludes_master_tenant_and_distinguishes_active_and_inactive_businesses()
    {
        await using var db = await OpenAsync();
        var master = DefaultSeedData.MasterTenant.Id;
        db.Tenants.Add(new Tenant(master, null, "Platform", "master", "localhost")
        {
            IsActive = true, CreatedAt = new DateTime(2026, 1, 1), RowVersion = [1],
        });
        for (var index = 0; index < 8; index++)
        {
            db.Tenants.Add(new Tenant(Guid.NewGuid(), master,
                "Business " + index, "tenant-" + index, "example.test")
            {
                IsActive = index < 3,
                CreatedAt = new DateTime(2026, 1, 2).AddDays(index),
                RowVersion = [1],
            });
        }
        await db.SaveChangesAsync();

        var actual = await new SuperDashboardQueryHandler(db).Handle(new(), default);
        Assert.Equal(8, actual.TotalTenants);
        Assert.Equal(3, actual.ActiveTenants);
        Assert.Equal(5, actual.InactiveTenants);
        Assert.Equal(6, actual.RecentTenants.Count);
        Assert.DoesNotContain(actual.RecentTenants, x => x.Id == master);
        Assert.Equal("tenant-7", actual.RecentTenants[0].Slug);
        Assert.Equal("tenant-2", actual.RecentTenants[^1].Slug);
    }

    [Fact]
    public async Task Reports_real_user_and_role_counts_including_empty_catalog()
    {
        await using var db = await OpenAsync();
        var initial = await new SuperDashboardQueryHandler(db).Handle(new(), default);
        Assert.Equal(0, initial.TotalTenants);
        Assert.Equal(0, initial.ActiveTenants);
        Assert.Empty(initial.RecentTenants);

        var master = DefaultSeedData.MasterTenant.Id;
        var system = NewUser(null, UserType.System, true);
        var tenant = NewUser(Guid.NewGuid(), UserType.Tenant, false);
        db.Users.AddRange(system, tenant);
        db.Roles.Add(new Role(master, "Admin", null) { RowVersion = [1] });
        await db.SaveChangesAsync();

        var actual = await new SuperDashboardQueryHandler(db).Handle(new(), default);
        Assert.Equal(2, actual.TotalUsers);
        Assert.Equal(1, actual.ActiveUsers);
        Assert.Equal(1, actual.SystemUsers);
        Assert.Equal(1, actual.TenantUsers);
        Assert.Equal(1, actual.TotalRoles);
        Assert.Equal(0, actual.TotalPermissions);
    }

    private static User NewUser(Guid? tenantId, UserType kind, bool active)
    {
        var id = Guid.NewGuid();
        return new User(id, tenantId, id.ToString("N"), id + "@example.test",
            "System", "Tester", kind)
        {
            IsActive = active,
            RowVersion = [1],
            PasswordHash = "test",
            NormalizedUserName = id.ToString("N").ToUpperInvariant(),
            NormalizedEmail = (id + "@example.test").ToUpperInvariant(),
        };
    }

    private static async Task<IdentityDbContext> OpenAsync()
    {
        var opts = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new IdentityDbContext(opts, new TenantContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
