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

    public static Task SignInAsync(HttpContext httpContext, Guid userId, string email)
    {
        var cap = DateTimeOffset.UtcNow.Add(SessionCap).ToUnixTimeSeconds();

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, email),
                new Claim(SessionCapClaimType, cap.ToString(CultureInfo.InvariantCulture))
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
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
