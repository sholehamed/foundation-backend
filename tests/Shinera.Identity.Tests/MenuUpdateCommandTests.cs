using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Features.Menus.Commands;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Xunit;

public sealed class MenuUpdateCommandTests
{
    [Fact]
    public async Task Update_changes_menu_and_uses_frontend_parent_route_and_external_url_contract()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"menu-update-{Guid.NewGuid():N}").Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        var categoryId = Guid.NewGuid();
        var parent = new Menu(Guid.NewGuid())
        {
            CategoryId = categoryId, Title = "Parent", Icon = "folder",
            IsActive = true, Order = 0
        };
        var menu = new Menu(Guid.NewGuid())
        {
            CategoryId = categoryId, Title = "Old", Icon = "home",
            Route = "/system/old", IsActive = true, Order = 1
        };
        db.Menus.AddRange(parent, menu);
        await db.SaveChangesAsync();

        var request = new MenuUpdateCommand
        {
            Id = menu.Id,
            CategoryId = categoryId,
            ParentId = parent.Id,
            Title = "New",
            Icon = "group",
            Order = 7,
            Route = "/system/users",
            PermissionId = null,
            IsActive = true,
            IsHidden = true
        };
        await new MenuUpdateCommandHandler(db).Handle(request, CancellationToken.None);

        var saved = await db.Menus.AsNoTracking().SingleAsync(x => x.Id == menu.Id);
        Assert.Equal(parent.Id, saved.ParentId);
        Assert.Equal("New", saved.Title);
        Assert.Equal("group", saved.Icon);
        Assert.Equal((short)7, saved.Order);
        Assert.Equal("/system/users", saved.Route);
        Assert.Null(saved.ExternalUrl);
        Assert.True(saved.IsHidden);
        Assert.Single(await db.Menus.AsNoTracking().Where(x => x.Id == parent.Id).ToListAsync());
        Assert.Equal("Parent", (await db.Menus.AsNoTracking().SingleAsync(x => x.Id == parent.Id)).Title);

        // Editing the same menu must support the external-link form too.
        request.ParentId = null;
        request.Route = null;
        request.ExternalUrl = "https://docs.example.test/";
        request.IsActive = false;
        await new MenuUpdateCommandHandler(db).Handle(request, CancellationToken.None);
        var external = await db.Menus.AsNoTracking().SingleAsync(x => x.Id == menu.Id);
        Assert.Null(external.ParentId);
        Assert.Null(external.Route);
        Assert.Equal("https://docs.example.test/", external.ExternalUrl);
        Assert.False(external.IsActive);
    }

    [Fact]
    public async Task Missing_menu_throws_not_found_without_mutating_other_records()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"menu-missing-{Guid.NewGuid():N}").Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        var existing = new Menu(Guid.NewGuid())
        {
            CategoryId = Guid.NewGuid(), Title = "Untouched",
            Icon = "menu", IsActive = true
        };
        db.Menus.Add(existing);
        await db.SaveChangesAsync();

        var request = new MenuUpdateCommand
        {
            Id = Guid.NewGuid(), CategoryId = existing.CategoryId,
            Title = "Unexpected", Icon = "delete",
            Route = "/system/other", IsActive = false
        };
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new MenuUpdateCommandHandler(db).Handle(request, CancellationToken.None));

        var saved = await db.Menus.AsNoTracking().SingleAsync();
        Assert.Equal("Untouched", saved.Title);
        Assert.True(saved.IsActive);
    }
}
