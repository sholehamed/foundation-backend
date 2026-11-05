using Modules.System.Identity.Application.Abstractions;

public sealed class TenantContext : ITenantContext
{
    private Guid[] _readable = [];
    private Guid[] _writable = [];
    private int _disableDepth;

    public Guid? UserId { get; private set; }
    public Guid? HomeTenantId { get; private set; }
    public Guid? ActiveTenantId { get; private set; }

    public bool IsSystemContext { get; private set; }
    public bool IsSuperAdmin { get; private set; }

    public IReadOnlyCollection<Guid> ReadableTenantIds => _readable;
    public IReadOnlyCollection<Guid> WritableTenantIds => _writable;

    public Guid[] ReadScope => _readable;
    public bool IsFilterDisabled => _disableDepth > 0;

    public void InitializeSystem(
        Guid userId,
        bool isSuperAdmin)
    {
        UserId = userId;
        IsSystemContext = true;
        IsSuperAdmin = isSuperAdmin;

        HomeTenantId = null;
        ActiveTenantId = null;

        _readable = [];
        _writable = [];
    }

    public void InitializeTenant(
        Guid userId,
        Guid homeTenantId,
        Guid[] readable,
        Guid[] writable,
        Guid activeTenantId)
    {
        UserId = userId;
        IsSystemContext = false;
        IsSuperAdmin = false;

        HomeTenantId = homeTenantId;
        ActiveTenantId = activeTenantId;

        _readable = readable;
        _writable = writable;
    }

    public IDisposable DisableFilter()
    {
        _disableDepth++;
        return new Scope(this);
    }

    private sealed class Scope(TenantContext context) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            context._disableDepth--;
        }
    }
}