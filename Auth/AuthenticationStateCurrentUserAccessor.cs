using System.Security.Claims;
using _10xnotes.Data;
using Microsoft.AspNetCore.Components.Authorization;

namespace _10xnotes.Auth;

/// <summary>
/// Resolves the current user from Blazor's authentication state.
/// </summary>
/// <remarks>
/// Deliberately NOT <c>IHttpContextAccessor</c>: <c>HttpContext</c> is only meaningful during
/// the initial render, so across an interactive Server circuit it is null or invalid. Sourcing
/// identity from it would make every owner-scoped query return zero rows for a user who is in
/// fact signed in — the HIGH finding from the F-01 implementation review.
/// </remarks>
public sealed class AuthenticationStateCurrentUserAccessor(AuthenticationStateProvider authenticationStateProvider)
    : ICurrentUserAccessor
{
    public async ValueTask<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;

        // The cap is re-checked here as well as in SessionCapAuthenticationStateProvider: the
        // provider's timer only runs for a live circuit, and the data layer must fail closed on
        // every path, including static SSR.
        if (user.Identity?.IsAuthenticated != true || AuthCookie.IsPastSessionCap(user, DateTimeOffset.UtcNow))
        {
            return null;
        }

        // Supabase issues the user id in the "sub" claim; ASP.NET maps it to NameIdentifier.
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
