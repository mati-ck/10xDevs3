using _10xnotes.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the one DI invariant that decides whether a signed-in user sees their own data on the
/// pages that render without a circuit.
/// </summary>
/// <remarks>
/// On static SSR — which is how <c>/</c> and <c>/materials</c> render — the framework pushes
/// <c>HttpContext.User</c> in through <see cref="IHostEnvironmentAuthenticationStateProvider"/>,
/// while <c>AuthenticationStateCurrentUserAccessor</c> reads it back through
/// <see cref="AuthenticationStateProvider"/>. The data layer's owner filter hangs off that read.
/// If the two names resolve to different instances, the push lands on an object nobody reads and
/// every list renders empty for a user who has rows — a failure that is safe but reads exactly
/// like data loss, and that no existing test would catch: the accessor's own tests supply a stub
/// provider and never resolve the real container.
/// </remarks>
public sealed class AuthenticationStateWiringTests
{
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSessionCapAuthenticationState();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Both_names_resolve_to_the_same_instance_within_a_scope()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();

        var read = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var written = scope.ServiceProvider.GetRequiredService<IHostEnvironmentAuthenticationStateProvider>();

        Assert.Same(read, written);
    }

    [Fact]
    public void The_shared_instance_is_the_applications_own_provider()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();

        Assert.IsType<SessionCapAuthenticationStateProvider>(
            scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>());
    }

    /// <summary>
    /// Scoped, not singleton: the principal belongs to one request or one circuit, and a shared
    /// instance would leak one user's identity into another's page.
    /// </summary>
    [Fact]
    public void Separate_scopes_get_separate_instances()
    {
        using var provider = BuildContainer();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<AuthenticationStateProvider>(),
            second.ServiceProvider.GetRequiredService<AuthenticationStateProvider>());
    }
}
