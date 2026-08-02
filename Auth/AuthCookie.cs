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
    public static Task SignInAsync(HttpContext httpContext, Guid userId, string email)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, email)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
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
