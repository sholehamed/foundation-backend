using System.Security.Claims;

namespace Modules.System.Identity.Application.Abstractions;

public interface IIdentitySessionValidator
{
    /// <summary>
    /// Revalidates the identity cookie against current account, tenant and security-stamp
    /// state, then rebuilds a fresh principal from current scoped assignments.
    /// </summary>
    Task<ClaimsPrincipal?> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
