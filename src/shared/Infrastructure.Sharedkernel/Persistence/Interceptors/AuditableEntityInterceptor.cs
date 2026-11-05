using Application.SharedKernel.Abstractions;
using Domain.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.SharedKernel.Persistence.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _auth;

    public AuditableEntityInterceptor(ICurrentUser auth)
    {
        this._auth = auth;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);

    }

    public void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        var userId = _auth.UserId;
        var userIp = _auth.IpAddress;
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.Create(userId, userIp);
            }
            if (entry.State == EntityState.Modified || entry.State == EntityState.Added)
            {
                entry.Entity.Modify(userId, userIp);

            }

        }
        foreach (var entry in context.ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State == EntityState.Deleted)
            {
                // Authorization assignment links must be removed physically:
                // soft-deleted GroupRole rows would continue to inherit
                // permissions and block re-grant under the unique index.
                // Their history is recorded by the transactional grant audit.
                // TenantSaveChangesInterceptor still validates exact write intent.
                if (entry.Entity is IHardDeleteAssignment)
                    continue;

                entry.Entity.Delete(userId, userIp);
                entry.State = EntityState.Modified;
            }
        }
    }


}
