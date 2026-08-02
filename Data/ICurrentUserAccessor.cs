namespace _10xnotes.Data;

/// <summary>
/// Single source of "who is asking" for the data layer.
/// <para>
/// Asynchronous because Blazor's <c>AuthenticationStateProvider</c> is — which is also why
/// this cannot be a plain property read inside a DbContext constructor.
/// </para>
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// The signed-in user's Supabase <c>auth.users.id</c>, or <c>null</c> when no user is
    /// authenticated. Callers treat <c>null</c> as "see nothing", never as "see everything".
    /// </summary>
    ValueTask<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default);
}
