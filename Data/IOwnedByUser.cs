namespace _10xnotes.Data;

/// <summary>
/// Marks an entity as belonging to exactly one user.
/// <para>
/// Every entity implementing this interface is automatically scoped by
/// <see cref="AppDbContext.CurrentUserId"/> through a global query filter, and has its
/// <see cref="OwnerId"/> stamped on insert. This is how the PRD privacy guardrail
/// ("one user's material is never visible to another") is enforced in the data layer —
/// so new user-owned tables inherit isolation by implementing this interface and nothing else.
/// </para>
/// </summary>
public interface IOwnedByUser
{
    /// <summary>The Supabase <c>auth.users.id</c> of the owning user.</summary>
    Guid OwnerId { get; set; }
}
