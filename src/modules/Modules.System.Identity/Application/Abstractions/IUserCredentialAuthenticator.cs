using System.Security.Claims;

namespace Modules.System.Identity.Application.Abstractions;

public enum IdentityLoginContext
{
    System,
    Tenant
}

public interface IUserCredentialAuthenticator
{
    Task<ClaimsPrincipal?> AuthenticateAsync(
        string username,
        string password,
        IdentityLoginContext context,
        string? tenantSlug,
        CancellationToken cancellationToken = default);
}
