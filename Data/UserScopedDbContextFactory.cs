using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Data;

/// <summary>
/// Creates an <see cref="AppDbContext"/> per operation with the *current* user applied.
/// </summary>
/// <remarks>
/// This is the only sanctioned way for application code to obtain a context. A context resolved
/// straight from DI has <see cref="AppDbContext.CurrentUserId"/> unset (<see cref="Guid.Empty"/>),
/// so it sees nothing — fail-closed, but useless for real work.
/// <para>
/// Per-operation rather than per-circuit: a Blazor Server scope lives as long as the circuit,
/// so a context held across it would keep filtering by whoever was signed in when it was created.
/// </para>
/// </remarks>
public sealed class UserScopedDbContextFactory(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICurrentUserAccessor currentUserAccessor)
{
    /// <summary>
    /// Creates a context scoped to the signed-in user. The caller owns disposal.
    /// </summary>
    public async Task<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUserAccessor.GetUserIdAsync(cancellationToken);
        var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.CurrentUserId = userId ?? Guid.Empty;
        return context;
    }
}
