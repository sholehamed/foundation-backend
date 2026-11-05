using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Services
{
    public sealed class TenantAccessResolver(
     IIdentityDbContext db,
     IDateTimeProvider clock) : ITenantAccessResolver
    {
        public async Task<(Guid[] Readable, Guid[] Writable)> ResolveAsync(
               Guid userId,
               Guid homeTenantId,
               CancellationToken ct)
        {
            var tenantIsActive = await db.Tenants
       .AsNoTracking()
       .AnyAsync(
           x => x.Id == homeTenantId && x.IsActive,
           ct);

            if (!tenantIsActive)
                return ([], []);

            // پیش‌فرض: فقط تننت خودش
            var read = new HashSet<Guid> { homeTenantId };
            var write = new HashSet<Guid> { homeTenantId };

            var now = clock.UtcNow;

            var grants = await db.UserTenantAccess.AsNoTracking()
                .Where(a => a.UserId == userId
                         && (a.ExpiresAt == null || a.ExpiresAt > now))
                .Select(a => new
                {
                    a.TenantId,
                    a.CanRead,
                    a.CanWrite,
                    a.IncludeDescendants,
                    a.IsDenied
                })
                .ToListAsync(ct);

            if (grants.Count > 0)
            {
                var roots = grants.Select(g => g.TenantId).Distinct().ToArray();

                // یک رفت‌وبرگشت برای همه زیردرخت‌ها
                var closures = await db.TenantClosure.AsNoTracking()
                    .Where(c => roots.Contains(c.AncestorTenantId)
                             && c.DescendantTenant!.IsActive)
                    .Select(c => new { c.AncestorTenantId, c.DescendantTenantId, c.Depth })
                    .ToListAsync(ct);

                var byRoot = closures
                    .GroupBy(c => c.AncestorTenantId)
                    .ToDictionary(g => g.Key, g => g.ToArray());

                var denyRead = new HashSet<Guid>();
                var denyWrite = new HashSet<Guid>();

                foreach (var g in grants)
                {
                    if (!byRoot.TryGetValue(g.TenantId, out var nodes)) continue;

                    var targets = g.IncludeDescendants
                        ? nodes.Select(n => n.DescendantTenantId)
                        : nodes.Where(n => n.Depth == 0).Select(n => n.DescendantTenantId);

                    foreach (var id in targets)
                    {
                        if (g.IsDenied)
                        {
                            if (g.CanRead) denyRead.Add(id);
                            if (g.CanWrite) denyWrite.Add(id);
                        }
                        else
                        {
                            if (g.CanRead) read.Add(id);
                            if (g.CanWrite) write.Add(id);
                        }
                    }
                }

                // Deny همیشه بر Allow غالب است
                read.ExceptWith(denyRead);
                write.ExceptWith(denyWrite);
            }

            write.IntersectWith(read);                 // نوشتن بدون خواندن بی‌معناست

            var r = read.ToArray(); Array.Sort(r);
            var w = write.ToArray(); Array.Sort(w);
            return (r, w);
        }
    }

}
