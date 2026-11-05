using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        // Only the startup seeder can enter this infrastructure-owned bypass.
        // A normal query filter override does NOT authorize writes.
        using var bootstrapWrites = scope.ServiceProvider
            .GetRequiredService<Interceptors.TrustedTenantWriteScope>()
            .BeginBootstrapWrites();

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await MigrateForStartupAsync(db, environment.IsProduction(),
            configuration.GetValue<bool>(
                "Identity:PlatformPermissions:AllowCodeUniquenessMigration"));

        var contributors = scope.ServiceProvider.GetServices<ISeedContributor>();

        foreach (var contributor in contributors.OrderBy(x => x.Order))
        {
            await contributor.SeedAsync(scope.ServiceProvider);
        }
    }

    /// <summary>
    /// Existing deployments auto-run Identity migrations during API startup.
    /// Never auto-apply the new table-locking, legacy-data-sensitive UNIQUE
    /// Permission Code migration in Production without a reviewed rollout.
    /// Defer at the last approved migration; DO NOT auto-downgrade databases
    /// where the unique index has already been installed.
    /// </summary>
    public static async Task MigrateForStartupAsync(
        IdentityDbContext db, bool isProduction,
        bool allowCodeUniquenessMigration, CancellationToken ct = default)
    {
        const string codeUniqueness = "20261024200000_permissioncodeuniqueness";
        const string lastApproved = "20261023210000_platformaccessdenialaudit";

        if (isProduction && !allowCodeUniquenessMigration)
        {
            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            if (!applied.Contains(codeUniqueness, StringComparer.Ordinal))
            {
                // Apply earlier approved migrations, but never auto-apply the
                // schema that can stop startup on dirty historical codes.
                // All later migrations also wait until the reviewed cutover.
                await db.Database.MigrateAsync(lastApproved, ct);
                return;
            }
        }

        await db.Database.MigrateAsync(cancellationToken: ct);
    }
}

public interface ISeedContributor
{
    int Order { get; }
    Task SeedAsync(IServiceProvider serviceProvider);
}
