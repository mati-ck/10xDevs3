namespace _10xnotes.Data.Entities;

/// <summary>
/// One line in the append-only ledger the PRD's "75% of AI notes are accepted" is measured from.
/// </summary>
/// <remarks>
/// The notes table cannot answer that question on its own. With exactly one note per material,
/// saving again overwrites the previous row — so a user who accepted five drafts for one material
/// leaves one row behind, and every acceptance but the last is gone. This ledger is never updated
/// and never deleted, so the count survives whatever happens to the note.
/// <para>
/// The two counts must measure the same population, which is why each event has exactly one
/// moment it may be written:
/// <list type="bullet">
/// <item><see cref="NoteEventKind.Generated"/> — only when a stream completes successfully. A
/// failure, a timeout or a cancellation produces nothing anyone could accept, so counting it
/// would depress the ratio for something that is not the model's output quality.</item>
/// <item><see cref="NoteEventKind.Saved"/> — only when a *freshly generated* draft is accepted,
/// never when an already-saved note is re-saved from <c>/notes/{id}</c>. Otherwise fixing a typo
/// raises the numerator against an unchanged denominator, and the ratio can pass 100%.</item>
/// </list>
/// That split is why <c>NoteService</c> has separate <c>AcceptAsync</c> and <c>UpdateAsync</c>
/// methods rather than one method with a flag.
/// </para>
/// <para>
/// Nothing here reads the ledger. It is written for a SQL query the product owner runs against
/// the database; a UI for it is explicitly out of scope for the MVP.
/// </para>
/// </remarks>
public sealed class NoteEvent : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>auth.users.id</c>; not unique — a user generates many notes.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Which material the event concerned — a plain <see cref="Guid"/> with no foreign key.
    /// </summary>
    /// <remarks>
    /// Deliberately unconstrained. A foreign key would make the ledger deletable along with the
    /// material, so the acceptance measurement would erase itself the moment somebody removed
    /// their source (S-06). It also keeps the ledger clear of the still-open question of what
    /// deleting a material should do to the note attached to it.
    /// </remarks>
    public Guid SourceMaterialId { get; set; }

    public NoteEventKind Kind { get; set; }

    /// <summary>The prompt version the draft came from, so a ratio can be attributed to one.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>The provider-qualified model id that produced the draft.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Characters in the model's draft.</summary>
    public int DraftLength { get; set; }

    /// <summary>
    /// Characters in what the user actually saved; zero on a
    /// <see cref="NoteEventKind.Generated"/> row, which has nothing saved yet.
    /// </summary>
    /// <remarks>
    /// Stored next to <see cref="DraftLength"/> so "accepted" can be graded rather than merely
    /// counted — an acceptance that doubled the length is a different signal from one that
    /// changed nothing.
    /// </remarks>
    public int SavedLength { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
