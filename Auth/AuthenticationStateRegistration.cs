using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace _10xnotes.Auth;

/// <summary>
/// Registers the application's authentication-state provider under every name that has to resolve
/// to the same instance.
/// </summary>
/// <remarks>
/// This exists as a method rather than three lines in <c>Program.cs</c> because the guarantee it
/// carries is testable only if something can call it. The static-SSR path pushes the principal in
/// through <see cref="IHostEnvironmentAuthenticationStateProvider"/> while components read it back
/// through <see cref="AuthenticationStateProvider"/>; if those two names ever resolve to separate
/// instances, every page renders as if the user were anonymous while the wiring still looks
/// correct. That failure is fail-closed but indistinguishable from data loss — a signed-in user
/// with notes is told they have none — which is why it is pinned by
/// <c>AuthenticationStateWiringTests</c> rather than by the comment that used to stand here.
/// <para>
/// Resolving the concrete type rather than casting the interface keeps the guarantee even if
/// something later registers a different <see cref="AuthenticationStateProvider"/>.
/// </para>
/// </remarks>
public static class AuthenticationStateRegistration
{
    public static IServiceCollection AddSessionCapAuthenticationState(this IServiceCollection services)
    {
        services.AddScoped<SessionCapAuthenticationStateProvider>();

        services.AddScoped<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<SessionCapAuthenticationStateProvider>());

        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp =>
            sp.GetRequiredService<SessionCapAuthenticationStateProvider>());

        return services;
    }
}
