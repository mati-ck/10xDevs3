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
        return await CreateForAsync(userId ?? Guid.Empty, cancellationToken);
    }

    /// <summary>
    /// Creates a context scoped to <paramref name="userId"/> rather than to whoever the accessor
    /// reports. The caller owns disposal.
    /// </summary>
    /// <remarks>
    /// For the one moment where there is a verified user but no signed-in one: the sign-in POST,
    /// between GoTrue confirming the credentials and the auth cookie being written. The accessor
    /// reads Blazor's authentication state, which static SSR snapshots from
    /// <c>HttpContext.User</c> before the component renders — so during that POST it reports
    /// nobody, no matter where in the handler it is called and no matter that
    /// <c>SignInAsync</c> has already run (writing the cookie does not reassign
    /// <c>HttpContext.User</c>). A read that needs the user's own row there has to say which user.
    /// <para>
    /// This is not a hole in the ownership model: the context is scoped exactly as
    /// <see cref="CreateAsync"/> scopes it, so the global query filter and <c>StampOwners</c>
    /// apply unchanged — the only difference is where the id came from. It must come from a
    /// source that has already proved the identity (GoTrue's response), never from a route value,
    /// a form field or a query string, or this becomes a way to read another user's rows by
    /// asking. Prefer <see cref="CreateAsync"/> everywhere the accessor can answer.
    /// </para>
    /// </remarks>
    public async Task<AppDbContext> CreateForAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        context.CurrentUserId = userId;
        return context;
    }
}
