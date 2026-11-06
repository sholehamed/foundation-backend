using System.Security.Claims;

namespace Modules.System.Bff;

/// <summary>
/// A host-provided authorization boundary. The BFF module must not depend
/// on the Identity module's database or persistence model.
/// </summary>
public interface IBffSessionAccessValidator
{
    Task<BffSessionAccessSnapshot?> CaptureAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<bool> IsCurrentAsync(
        ClaimsPrincipal principal, BffSessionAccessSnapshot issued,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Captured only in the protected server-side AuthenticationTicket properties.
/// Never send these values to a browser, access token, or API response.
/// </summary>
public sealed record BffSessionAccessSnapshot(
    string SecurityStamp, string GrantsFingerprint);
