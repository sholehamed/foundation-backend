namespace Modules.System.Identity.Application.Abstractions
{
    public interface ITenantContext
    {
        Guid? UserId { get; }
        Guid? HomeTenantId { get; }
        Guid? ActiveTenantId { get; }

        bool IsSystemContext { get; }
        bool IsSuperAdmin { get; }

        IReadOnlyCollection<Guid> ReadableTenantIds { get; }
        IReadOnlyCollection<Guid> WritableTenantIds { get; }

        bool IsFilterDisabled { get; }
        Guid[] ReadScope { get; }

        void InitializeSystem(
            Guid userId,
            bool isSuperAdmin);

        void InitializeTenant(
            Guid userId,
            Guid homeTenantId,
            Guid[] readable,
            Guid[] writable,
            Guid activeTenantId);

        IDisposable DisableFilter();
    }

}
