using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace _10xnotes.Auth;

/// <summary>
/// Turns a verified Supabase identity into this application's session, and back out again.
/// </summary>
/// <remarks>
/// The GoTrue access and refresh tokens are deliberately discarded: the cookie is the only
/// session concept in the app. <c>OwnerId</c> in the data layer is already
/// <c>auth.users.id</c>, so the <see cref="ClaimTypes.NameIdentifier"/> claim written here is
/// exactly what the owner-scoping query filter reads back.
/// </remarks>
public static class AuthCookie
{
    /// <summary>
    /// How long the cookie stays valid without activity. Sliding: ordinary use renews it.
    /// </summary>
    public static readonly TimeSpan CookieWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// The latest instant a single sign-in may still be trusted, regardless of activity.
    /// </summary>
    /// <remarks>
    /// Deliberately a different number from <see cref="CookieWindow"/>, and deliberately larger.
    /// A claim stamped at sign-in cannot slide, so it cannot represent "when the cookie expires" —
    /// that instant moves with every request and a SignalR circuit, which holds a snapshot of the
    /// principal taken when it opened, has no way to re-read it. What the claim *can* express is
    /// an outer bound on the sign-in itself, which is what stops a circuit staying authenticated
    /// for as long as its connection happens to survive.
    /// </remarks>
    public static readonly TimeSpan SessionCap = TimeSpan.FromDays(30);

    /// <summary>Unix seconds after which this sign-in is no longer trusted.</summary>
    public const string SessionCapClaimType = "session_cap";

    /// <summary>
    /// The name the user chose to be shown as, when they have chosen one.
    /// </summary>
    /// <remarks>
    /// A claim rather than a query, so the nav costs no database round trip on any render. Its own
    /// type rather than overwriting <see cref="ClaimTypes.Name"/>: the email has to stay reachable
    /// for the profile page to show it and for the change-password re-authentication to use it,
    /// and a principal that has lost its email would have lost it silently.
    /// <para>
    /// Absent when the user has not set a name. A cookie issued by a version that predates this
    /// claim simply lacks it, and <see cref="DisplayNameOrEmail"/> falls back — so no existing
    /// cookie is invalidated by this claim appearing, and none is invalidated by it disappearing
    /// on a rollback.
    /// </para>
    /// </remarks>
    public const string DisplayNameClaimType = "display_name";

    /// <summary>
    /// Builds the principal the cookie carries. The single place the claim set is defined, so the
    /// sign-in path and the re-issue path cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Public so the claim set can be asserted on without an <c>HttpContext</c>: what a cookie
    /// carries is a decision worth testing, and every caller that writes one goes through here.
    /// </remarks>
    public static ClaimsPrincipal BuildPrincipal(Guid userId, string email, string? displayName = null)
    {
        var cap = DateTimeOffset.UtcNow.Add(SessionCap).ToUnixTimeSeconds();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, email),
            new(SessionCapClaimType, cap.ToString(CultureInfo.InvariantCulture))
        };

        // Omitted rather than written empty when there is no name: absent and "" would be two
        // spellings of the same thing, and only one of them makes DisplayNameOrEmail fall back.
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            claims.Add(new Claim(DisplayNameClaimType, displayName));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Task SignInAsync(
        HttpContext httpContext,
        Guid userId,
        string email,
        string? displayName = null) =>
        httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(userId, email, displayName),
            new AuthenticationProperties { IsPersistent = true });

    /// <summary>
    /// Re-issues the cookie for the user who is already signed in, carrying a changed display
    /// name. Returns <c>false</c> when the current principal has no usable id, in which case
    /// nothing is written.
    /// </summary>
    /// <remarks>
    /// The nav renders from the cookie, so a saved name that is not written back into it would
    /// not appear until the next sign-in. This must run before the response starts, which is why
    /// the profile page is static SSR.
    /// <para>
    /// <strong>Side effect:</strong> this restamps the session cap, so editing a display name
    /// buys another <see cref="SessionCap"/> from now. Accepted rather than fixed — see the
    /// plan's Critical Implementation Details. Do not "correct" it by adding a cap check here:
    /// <c>OnValidatePrincipal</c> never fires on a fresh sign-in, so a check would not preserve
    /// the original cap either. Preserving it would mean carrying the old claim forward
    /// deliberately, which is a decision, not a bug fix.
    /// </para>
    /// </remarks>
    public static async Task<bool> ReissueWithDisplayNameAsync(
        HttpContext httpContext,
        string? displayName)
    {
        var principal = httpContext.User;

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        await SignInAsync(
            httpContext,
            userId,
            principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            displayName);

        return true;
    }

    /// <summary>
    /// What to call this user on screen: their display name when they set one, their email
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The rule lives here rather than in the nav markup so it is testable without bUnit, and so
    /// a second surface that needs it cannot implement the fallback differently.
    /// </remarks>
    public static string? DisplayNameOrEmail(ClaimsPrincipal principal)
    {
        var displayName = principal.FindFirstValue(DisplayNameClaimType);

        return string.IsNullOrWhiteSpace(displayName)
            ? principal.Identity?.Name
            : displayName;
    }

    /// <summary>
    /// Whether this sign-in has passed its cap at <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// "Now" is a parameter rather than a call to <see cref="DateTimeOffset.UtcNow"/> inside, so
    /// the rule is testable without an <c>HttpContext</c>, a circuit, or a clock that has to be
    /// waited out.
    /// <para>
    /// A principal with no cap claim, or one that will not parse, is treated as past its cap.
    /// Cookies issued before the cap existed carry none, and retiring those sessions once is the
    /// right answer — exempting them would grant exactly the unbounded lifetime this exists to
    /// remove.
    /// </para>
    /// </remarks>
    public static bool IsPastSessionCap(ClaimsPrincipal principal, DateTimeOffset now)
    {
        var raw = principal.FindFirstValue(SessionCapClaimType);

        return !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var capUnixSeconds)
            || DateTimeOffset.FromUnixTimeSeconds(capUnixSeconds) <= now;
    }

    public static Task SignOutAsync(HttpContext httpContext) =>
        httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>
    /// Accepts only same-site relative paths, so a crafted <c>?returnUrl=</c> cannot bounce a
    /// freshly authenticated user to another host. <c>//evil.example</c> is protocol-relative
    /// and therefore rejected too.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
}
