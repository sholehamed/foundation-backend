using System.Security.Claims;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Abstractions;

public interface IIdentityPrincipalFactory
{
    Task<ClaimsPrincipal> CreateAsync(
        User user,
        IdentityLoginContext context,
        Tenant? tenant,
        long? authenticationTimeUnixSeconds,
        CancellationToken cancellationToken = default);
}
