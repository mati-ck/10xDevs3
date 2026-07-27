namespace _10xnotes.Data.Entities;

/// <summary>
/// The application-side record for a user whose identity lives in Supabase Auth
/// (<c>auth.users</c>). This is the first entity to use the ownership convention.
/// </summary>
/// <remarks>
/// Deliberately differs from Supabase's canonical <c>profiles.id = auth.users.id</c> shape:
/// a surrogate <see cref="Id"/> plus a separate <see cref="OwnerId"/> keeps one uniform
/// ownership convention across every table, so later entities (notes, source material)
/// need no special-casing. The unique index on <see cref="OwnerId"/> preserves
/// one-profile-per-user.
/// </remarks>
public sealed class Profile : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>auth.users.id</c>; unique.</summary>
    public Guid OwnerId { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
