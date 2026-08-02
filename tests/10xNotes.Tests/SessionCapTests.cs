using System.Globalization;
using System.Security.Claims;
using _10xnotes.Auth;
using _10xnotes.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the session cap: the rule that stops a SignalR circuit staying authenticated for as long
/// as its connection happens to survive.
/// </summary>
/// <remarks>
/// The real cap is 30 days, so none of this can be observed by waiting. Taking "now" as a
/// parameter is what makes the rule testable at all — every case here is arithmetic over a
/// principal, with no clock, no <c>HttpContext</c> and no circuit involved.
/// </remarks>
public sealed class SessionCapTests
{
    private static readonly DateTimeOffset SignInAt = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void A_session_inside_its_cap_is_still_trusted()
    {
        var principal = SignedInAt(SignInAt);

        Assert.False(AuthCookie.IsPastSessionCap(principal, SignInAt.Add(AuthCookie.SessionCap) - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_session_past_its_cap_is_not()
    {
        var principal = SignedInAt(SignInAt);

        Assert.True(AuthCookie.IsPastSessionCap(principal, SignInAt.Add(AuthCookie.SessionCap) + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void The_cap_instant_itself_counts_as_past()
    {
        // Boundary pinned deliberately: "<=" rather than "<" means a cap can never be met exactly
        // and then honoured for another interval.
        var principal = SignedInAt(SignInAt);

        Assert.True(AuthCookie.IsPastSessionCap(principal, SignInAt.Add(AuthCookie.SessionCap)));
    }

    [Fact]
    public void A_principal_with_no_cap_claim_is_past_it()
    {
        // Cookies issued before the cap existed carry no claim. Failing closed retires those
        // sessions once; exempting them would grant the unbounded lifetime this exists to remove.
        var principal = Authenticated([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())]);

        Assert.True(AuthCookie.IsPastSessionCap(principal, SignInAt));
    }

    [Fact]
    public void An_unparseable_cap_claim_is_past_it()
    {
        var principal = Authenticated([
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(AuthCookie.SessionCapClaimType, "not-a-number")
        ]);

        Assert.True(AuthCookie.IsPastSessionCap(principal, SignInAt));
    }

    [Fact]
    public async Task A_past_cap_principal_yields_no_user_id()
    {
        // The end-to-end consequence: the data layer must see nobody, so owner-scoped queries
        // return nothing rather than serving a session that should have been retired.
        var expired = SignedInAt(SignInAt - AuthCookie.SessionCap - TimeSpan.FromDays(1));
        var accessor = new AuthenticationStateCurrentUserAccessor(new StubProvider(expired));

        Assert.Null(await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task A_principal_inside_its_cap_still_yields_the_user_id()
    {
        var live = SignedInAt(DateTimeOffset.UtcNow);
        var accessor = new AuthenticationStateCurrentUserAccessor(new StubProvider(live));

        Assert.Equal(UserId, await accessor.GetUserIdAsync());
    }

    [Fact]
    public async Task Revalidation_retires_a_circuit_whose_session_is_past_its_cap()
    {
        var expired = SignedInAt(DateTimeOffset.UtcNow - AuthCookie.SessionCap - TimeSpan.FromDays(1));

        Assert.False(await ValidateAsync(expired));
    }

    [Fact]
    public async Task Revalidation_leaves_a_live_circuit_alone()
    {
        var live = SignedInAt(DateTimeOffset.UtcNow);

        Assert.True(await ValidateAsync(live));
    }

    [Fact]
    public async Task Revalidation_leaves_an_anonymous_circuit_alone()
    {
        // Anonymous has nothing to retire, and reporting it invalid would put the circuit into a
        // pointless invalidation loop. This is the one branch AuthCookie.IsPastSessionCap does not
        // decide — it would call a claim-less principal past its cap.
        Assert.True(await ValidateAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static async Task<bool> ValidateAsync(ClaimsPrincipal principal)
    {
        using var provider = new TestableProvider();

        return await provider.RevalidateAsync(new AuthenticationState(principal));
    }

    /// <summary>
    /// Exposes the protected revalidation hook. It has no other reachable caller: the loop that
    /// invokes it only runs inside a live SignalR circuit.
    /// </summary>
    private sealed class TestableProvider() : SessionCapAuthenticationStateProvider(NullLoggerFactory.Instance)
    {
        public Task<bool> RevalidateAsync(AuthenticationState state)
            => ValidateAuthenticationStateAsync(state, CancellationToken.None);
    }

    /// <summary>A principal shaped exactly as <c>AuthCookie.SignInAsync</c> writes it.</summary>
    private static ClaimsPrincipal SignedInAt(DateTimeOffset signedInAt)
    {
        var cap = signedInAt.Add(AuthCookie.SessionCap).ToUnixTimeSeconds();

        return Authenticated([
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(AuthCookie.SessionCapClaimType, cap.ToString(CultureInfo.InvariantCulture))
        ]);
    }

    private static ClaimsPrincipal Authenticated(Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    private sealed class StubProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }
}
