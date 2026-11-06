namespace Modules.System.Bff;

/// <summary>
/// Web-host provided current-account session revocation.
/// Called only after authenticating/host-validating a BFF cookie and validating
/// CSRF. Subject is always sourced from that verified principal, not the body.
/// Revokes existing BFF browser tickets; not upstream Identity refresh tokens.
/// </summary>
public interface IBffAccountSessionRevoker
{
    Task<int> RevokeAllAsync(Guid subjectId, CancellationToken cancellationToken = default);
}
