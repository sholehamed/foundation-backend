using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;

namespace Modules.System.Identity.Application.Features.Dashboard;

/// <summary>
/// Platform overview for an authorized SystemAdministrator. Tenant totals
/// exclude the platform's Master Tenant; no tenant-specific membership data
/// or financial/subscription assumptions are inferred.
/// </summary>
public sealed class SuperDashboardQuery : IQuery<SuperDashboardDto>;

public sealed record RecentTenantDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTime CreatedAt);

public sealed record SuperDashboardDto
{
    public int TotalTenants { get; init; }
    public int ActiveTenants { get; init; }
    public int InactiveTenants { get; init; }
    public int TotalUsers { get; init; }
    public int ActiveUsers { get; init; }
    public int SystemUsers { get; init; }
    public int TenantUsers { get; init; }
    public int TotalRoles { get; init; }
    public int TotalPermissions { get; init; }
    public int TotalGroups { get; init; }
    public int TotalModules { get; init; }
    public IReadOnlyList<RecentTenantDto> RecentTenants { get; init; } = [];
}

public sealed class SuperDashboardQueryHandler(IIdentityDbContext db)
    : IQueryHandler<SuperDashboardQuery, SuperDashboardDto>
{
    public async Task<SuperDashboardDto> Handle(
        SuperDashboardQuery request, CancellationToken cancellationToken)
    {
        // This handler is exposed ONLY behind the live SystemAdministrator
        // policy. Normal tenant endpoints must never run cross-tenant queries.
        var tenants = db.Tenants.AsNoTracking()
            .Where(x => x.Id != DefaultSeedData.MasterTenant.Id);
        var users = db.Users.AsNoTracking();
        var roles = db.Roles.AsNoTracking();

        var totalTenants = await tenants.CountAsync(cancellationToken);
        var activeTenants = await tenants.CountAsync(x => x.IsActive, cancellationToken);

        return new SuperDashboardDto
        {
            TotalTenants = totalTenants,
            ActiveTenants = activeTenants,
            InactiveTenants = totalTenants - activeTenants,
            TotalUsers = await users.CountAsync(cancellationToken),
            ActiveUsers = await users.CountAsync(x => x.IsActive, cancellationToken),
            SystemUsers = await users.CountAsync(x => x.UserType == UserType.System, cancellationToken),
            TenantUsers = await users.CountAsync(x => x.UserType == UserType.Tenant, cancellationToken),
            TotalRoles = await roles.CountAsync(cancellationToken),
            TotalPermissions = await db.Permissions.AsNoTracking().CountAsync(cancellationToken),
            TotalGroups = await db.Groups.AsNoTracking().CountAsync(cancellationToken),
            TotalModules = await db.Modules.AsNoTracking().CountAsync(cancellationToken),
            RecentTenants = await tenants
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Select(x => new RecentTenantDto(x.Id, x.Name, x.Slug, x.IsActive, x.CreatedAt))
                .Take(6)
                .ToListAsync(cancellationToken),
        };
    }
}
