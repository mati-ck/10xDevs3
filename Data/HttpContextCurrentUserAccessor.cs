using System.Security.Claims;

namespace _10xnotes.Data;

/// <summary>
/// Reads the current user from the request's claims principal.
/// <para>
/// Nothing populates those claims yet — authentication is F-02 — so this returns
/// <c>null</c> today, which makes every owner-scoped query return zero rows.
/// </para>
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserAccessor
{
    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Supabase issues the user id in the "sub" claim; ASP.NET maps it to NameIdentifier.
            var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }
}
