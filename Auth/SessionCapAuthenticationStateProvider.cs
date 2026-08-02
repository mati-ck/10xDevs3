using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace _10xnotes.Auth;

/// <summary>
/// Re-checks the session cap on a timer, so a circuit cannot stay authenticated indefinitely.
/// </summary>
/// <remarks>
/// Without this the default <c>ServerAuthenticationStateProvider</c> seeds the principal once,
/// from the HTTP request that opened the circuit, and never looks at it again — an authenticated
/// circuit then lives for as long as its connection does.
/// <para>
/// The check is claim arithmetic: no storage, no database, no network. That is the whole reason
/// the session stays stateless. The cost is that this cannot notice a cookie deleted or expired
/// mid-circuit — a sliding cookie's real expiry moves and the circuit holds only a snapshot — so
/// the cap is an outer bound, not a mirror of the cookie.
/// </para>
/// </remarks>
/// <remarks>
/// Not sealed so the tests can subclass it and reach the protected revalidation hook — verifying
/// the decision matters more than the keyword, and the hook has no visible caller to test through:
/// the loop only runs inside a live circuit.
/// </remarks>
public class SessionCapAuthenticationStateProvider(ILoggerFactory loggerFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <summary>
    /// Short enough that a retired session stops being usable promptly, long enough that the
    /// timer is invisible — the check itself costs nothing, so the interval is about how quickly
    /// the cap should bite, not about load.
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        var user = authenticationState.User;

        // An anonymous circuit has nothing to retire; returning false would put it into a
        // pointless invalidation loop.
        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(!AuthCookie.IsPastSessionCap(user, DateTimeOffset.UtcNow));
    }
}
