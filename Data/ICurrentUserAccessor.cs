namespace _10xnotes.Data;

/// <summary>
/// Single source of "who is asking" for the data layer.
/// <para>
/// Deliberately knows nothing about how authentication works, so F-02 can swap the
/// implementation (Supabase Auth sign-in exchanged for an ASP.NET cookie) without any
/// change to <see cref="AppDbContext"/> or the entities.
/// </para>
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// The signed-in user's Supabase <c>auth.users.id</c>, or <c>null</c> when no user is
    /// authenticated. Until F-02 lands this is always <c>null</c> — which is correct:
    /// an unauthenticated request must see nothing.
    /// </summary>
    Guid? UserId { get; }
}
