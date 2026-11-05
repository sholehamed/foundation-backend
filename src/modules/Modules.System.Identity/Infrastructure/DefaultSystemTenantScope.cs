using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence;

namespace Modules.System.Identity.Infrastructure;

/// <summary>Resolves the canonical master scope used for platform authorization assignments.</summary>
public sealed class DefaultSystemTenantScope : ISystemTenantScope
{
    public Guid MasterTenantId => DefaultSeedData.MasterTenant.Id;
}
