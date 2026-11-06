using Microsoft.AspNetCore.Authentication.Cookies;

namespace Modules.System.Bff;

/// <summary>
/// Explicit host-provided atomic server-session backend contract.
///
/// An implementation MUST guarantee a linearizable renew-if-active/revoke
/// boundary for a single session across all instances, including concurrency
/// with expiry and removal. Missing keys must not be re-created by renewal;
/// revocation must remain effective across restart, eviction and failover for
/// the maximal absolute lifetime of an issued session. Secret ticket data
/// must remain protected server-side, and cache/database outages must fail
/// closed instead of accepting an unverified ticket.
///
/// Implementations need provider-specific transactional/CAS or Lua/SQL logic
/// and integration tests; implementing this interface alone is not evidence
/// of these requirements. Production security sign-off is still required.
/// </summary>
public interface IBffAtomicTicketStore : ITicketStore
{
}
