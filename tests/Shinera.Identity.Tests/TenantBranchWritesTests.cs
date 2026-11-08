using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

public sealed class TenantBranchWritesTests
{
    [Fact]
    public async Task Local_owner_can_create_and_update_only_own_branch_and_preserve_main()
    {
        var tenantId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, canWrite: true);
        await using var db = await OpenAsync(current);
        await SeedOwnerAsync(db, tenantId, actor);
        db.Tenants.Add(new Tenant(foreignId, null, "Unrelated", "unrelated", "foreign.test")
            { RowVersion = [1] });
        var main = new Branch { TenantId = tenantId, Name = "Main", City = "Tehran",
            Address = "Main street", IsMain = true, RowVersion = [1] };
        var foreign = new Branch { TenantId = foreignId, Name = "Foreign", City = "Other",
            Address = "Secret", IsMain = true, RowVersion = [1] };
        db.Branches.AddRange(main, foreign);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var created = Assert.IsType<Ok<TenantWorkspace.WorkspaceBranchDto>>(
            await TenantWorkspace.CreateBranch(current, db, http,
                new TenantWorkspace.BranchWriteRequest(" North ", " Tehran ", " Street ", " 021 "), default));
        Assert.NotNull(created.Value);
        Assert.False(created.Value.IsMain);
        Assert.Equal("North", created.Value.Name);
        Assert.Equal("021", created.Value.Phone);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.True(await db.Branches.AnyAsync(x => x.Id == created.Value.Id &&
            x.TenantId == tenantId && !x.IsMain));

        var edited = Assert.IsType<Ok<TenantWorkspace.WorkspaceBranchDto>>(
            await TenantWorkspace.UpdateBranch(current, db, new DefaultHttpContext(), main.Id,
                new TenantWorkspace.BranchWriteRequest(" Updated main ", "Tehran", "Address", null), default));
        Assert.True(edited.Value!.IsMain);
        Assert.Equal("Updated main", edited.Value.Name);
        Assert.Equal(tenantId, (await db.Branches.FindAsync(main.Id))!.TenantId);

        Assert.IsType<NotFound>(await TenantWorkspace.UpdateBranch(
            current, db, new DefaultHttpContext(), foreign.Id,
            new TenantWorkspace.BranchWriteRequest("Attack", "Wrong", "Address", null), default));
        Assert.Equal("Foreign", (await db.Branches.FindAsync(foreign.Id))!.Name);
    }

    [Fact]
    public async Task Write_endpoints_deny_read_only_owner_non_owner_and_system_principal()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ownerScope = Current(tenantId, actor, canWrite: true);
        await using var db = await OpenAsync(ownerScope);
        await SeedOwnerAsync(db, tenantId, actor);
        db.Users.Add(User(staffId, tenantId, "staff"));
        await db.SaveChangesAsync();
        var request = new TenantWorkspace.BranchWriteRequest("Extra", "Tehran", "Address", null);

        var readOnlyOwner = Current(tenantId, actor, canWrite: false);
        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.CreateBranch(
            readOnlyOwner, db, new DefaultHttpContext(), request, default));
        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.UpdateBranch(
            readOnlyOwner, db, new DefaultHttpContext(), Guid.NewGuid(), request, default));

        var staff = Current(tenantId, staffId, canWrite: true);
        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.CreateBranch(
            staff, db, new DefaultHttpContext(), request, default));

        var system = new TenantContext();
        system.InitializeSystem(Guid.NewGuid(), true);
        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.CreateBranch(
            system, db, new DefaultHttpContext(), request, default));
        Assert.Empty(db.Branches);
    }

    [Fact]
    public async Task Rejects_blank_or_oversize_fields_and_cannot_change_main_flag()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, true);
        await using var db = await OpenAsync(current);
        await SeedOwnerAsync(db, tenantId, actor);
        var main = new Branch { TenantId = tenantId, Name = "Main", City = "Tehran",
            Address = "Old", IsMain = true, RowVersion = [1] };
        db.Branches.Add(main);
        await db.SaveChangesAsync();

        var invalid = await TenantWorkspace.CreateBranch(current, db, new DefaultHttpContext(),
            new TenantWorkspace.BranchWriteRequest(" ", "City", "Address", null), default);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalid).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
            await TenantWorkspace.UpdateBranch(current, db, new DefaultHttpContext(), main.Id,
                new TenantWorkspace.BranchWriteRequest(new string('x', 121), "City", "Address", null),
                default)).StatusCode);
        Assert.Equal("Main", (await db.Branches.FindAsync(main.Id))!.Name);
        Assert.True((await db.Branches.FindAsync(main.Id))!.IsMain);
        Assert.Single(db.Branches);
    }

    [Fact]
    public async Task Revoked_owner_role_fails_closed_for_new_mutations()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, true);
        await using var db = await OpenAsync(current);
        await SeedOwnerAsync(db, tenantId, actor);
        var request = new TenantWorkspace.BranchWriteRequest("Next", "City", "Address", null);
        var assignment = await db.UserRoles.SingleAsync();
        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync();

        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.CreateBranch(
            current, db, new DefaultHttpContext(), request, default));
        Assert.Empty(db.Branches);
    }

    private static async Task SeedOwnerAsync(IdentityDbContext db, Guid tenantId, Guid actorId)
    {
        db.Tenants.Add(new Tenant(tenantId, null, "Workspace", "workspace", "own.test")
            { RowVersion = [1], IsActive = true });
        db.Users.Add(User(actorId, tenantId, "owner"));
        var ownerRole = new Role(tenantId, "Owner", "Owner") { Scope = RoleScope.Tenant, RowVersion = [1] };
        db.Roles.Add(ownerRole);
        db.UserRoles.Add(new UserRole(tenantId, actorId, ownerRole.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();
    }

    private static User User(Guid id, Guid tenantId, string username) =>
        new(id, tenantId, username, username + "@test.local", "Sample", username, UserType.Tenant)
        {
            NormalizedUserName = username.ToUpperInvariant(),
            NormalizedEmail = username.ToUpperInvariant() + "@TEST.LOCAL",
            PasswordHash = "fixture-hash", RowVersion = [1], IsActive = true
        };

    private static TenantContext Current(Guid tenantId, Guid userId, bool canWrite)
    {
        var current = new TenantContext();
        current.InitializeTenant(userId, tenantId, [tenantId],
            canWrite ? [tenantId] : [], tenantId);
        return current;
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
