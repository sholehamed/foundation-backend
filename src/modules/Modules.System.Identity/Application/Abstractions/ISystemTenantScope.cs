namespace Modules.System.Identity.Application.Abstractions;

/// <summary>
/// The tenant scope that owns platform/System roles, groups and permission assignments.
/// It is deliberately separate from a tenant account's active workspace.
/// </summary>
public interface ISystemTenantScope
{
    Guid MasterTenantId { get; }
}
