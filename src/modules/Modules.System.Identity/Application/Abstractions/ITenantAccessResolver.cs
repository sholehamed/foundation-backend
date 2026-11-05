namespace Modules.System.Identity.Application.Abstractions
{
    public interface ITenantAccessResolver
    {
        Task<(Guid[] Readable, Guid[] Writable)> ResolveAsync(
            Guid userId,
            Guid homeTenantId,
            CancellationToken ct);
    }
}