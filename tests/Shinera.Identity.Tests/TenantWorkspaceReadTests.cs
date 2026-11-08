using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

public sealed class TenantWorkspaceReadTests
{
    private static void AssertForbidden(IResult result)
    {
        var response = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_only_the_authenticated_active_tenants_branches()
    {
        var owned = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        var current = TenantContext(owned);
        await using var db = await OpenAsync(current);

        db.Tenants.AddRange(
            new Tenant(owned, null, "Owned workspace", "owned", "owned.example.test")
                { RowVersion = [1], IsActive = true },
            new Tenant(unrelated, null, "Other workspace", "other", "other.example.test")
                { RowVersion = [1], IsActive = true });
        db.Branches.AddRange(
            new Branch { TenantId = owned, Name = "Other branch", City = "Tehran",
                Address = "Second", IsMain = false, RowVersion = [1] },
            new Branch { TenantId = owned, Name = "Main branch", City = "Tehran",
                Address = "First", IsMain = true, RowVersion = [1] },
            new Branch { TenantId = unrelated, Name = "Secret other tenant",
                City = "Another City", Address = "Protected", IsMain = true, RowVersion = [1] });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var response = await TenantWorkspace.GetBranches(current, db, http, default);
        var ok = Assert.IsType<Ok<List<TenantWorkspace.WorkspaceBranchDto>>>(response);
        Assert.NotNull(ok.Value);
        Assert.Equal(2, ok.Value.Count);
        Assert.Equal("Main branch", ok.Value[0].Name);
        Assert.All(ok.Value, x => Assert.DoesNotContain("Secret", x.Name));
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Rejects_system_or_unreadable_context_without_disclosing_branch_rows()
    {
        var owned = Guid.NewGuid();
        var ctx = TenantContext(owned);
        await using var db = await OpenAsync(ctx);
        db.Tenants.Add(new Tenant(owned, null, "Workspace", "workspace", "workspace.test")
            { IsActive = true, RowVersion = [1] });
        db.Branches.Add(new Branch { TenantId = owned, Name = "Private branch",
            IsMain = true, RowVersion = [1] });
        await db.SaveChangesAsync();

        var system = new TenantContext();
        system.InitializeSystem(Guid.NewGuid(), isSuperAdmin: true);
        AssertForbidden(await TenantWorkspace.GetBranches(
            system, db, new DefaultHttpContext(), default));

        var unreadable = new TenantContext();
        unreadable.InitializeTenant(Guid.NewGuid(), owned, [], [], owned);
        AssertForbidden(await TenantWorkspace.GetBranches(
            unreadable, db, new DefaultHttpContext(), default));

        var absent = Guid.NewGuid();
        var invalid = TenantContext(absent);
        Assert.IsType<NotFound>(await TenantWorkspace.GetBranches(
            invalid, db, new DefaultHttpContext(), default));
    }

    private static TenantContext TenantContext(Guid tenantId)
    {
        var context = new TenantContext();
        context.InitializeTenant(Guid.NewGuid(), tenantId, [tenantId], [tenantId], tenantId);
        return context;
    }

    private static async Task<IdentityDbContext> OpenAsync(TenantContext current)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new IdentityDbContext(options, current);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
