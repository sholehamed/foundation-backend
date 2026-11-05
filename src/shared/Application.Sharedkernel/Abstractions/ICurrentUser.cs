using System.Security.Claims;

namespace Application.SharedKernel.Abstractions
{
    public interface ICurrentUser
    {
        string UserAgent { get; }
        string? IpAddress { get; }
        bool IsAuthenticated { get; }

        Guid UserId { get; }

        Guid HomeTenantId { get; }

        string? UserName { get; }

        IEnumerable<Claim> GetClaims();
    }


}