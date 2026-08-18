namespace _10xnotes.Data.Entities;

/// <summary>
/// A note the user accepted by saving it. Exactly one per <see cref="SourceMaterial"/> — saving
/// again replaces it.
/// </summary>
/// <remarks>
/// Saving is what "accepted" means in this product, so this row is the acceptance. Nothing
/// reaches it until the user clicks save: abandoning a generated note leaves nothing behind, and
/// the source material is never touched by any of it.
/// <para>
/// The 1:1 relationship is enforced by a unique index on <see cref="SourceMaterialId"/>, not by
/// convention — see <c>AppDbContext.OnModelCreating</c>, which also explains why the foreign key
/// into <c>source_materials</c> is deliberately <c>NO ACTION</c>.
/// </para>
/// </remarks>
public sealed class Note : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>auth.users.id</c>; not unique — a user owns many notes.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The material this note summarises. Unique: one saved note per material.</summary>
    public Guid SourceMaterialId { get; set; }

    /// <summary>
    /// Derived from the material's title when the editor opens, and correctable by the user
    /// before saving. Required, so a note is never nameless in the list S-03 will build.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The note as the user left it — what they accepted.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The model's original draft, before the user edited it.
    /// </summary>
    /// <remarks>
    /// This column is what turns the PRD's "75% of AI notes are accepted" from a yes/no answer
    /// into a measurable "how much did the human have to fix". Without it, a note rewritten from
    /// scratch and a note accepted verbatim are the same row.
    /// </remarks>
    public string DraftContent { get; set; } = string.Empty;

    /// <summary>
    /// The <c>NotePrompt.Version</c> the draft came from. That constant has existed since S-01b
    /// specifically so acceptance could later be attributed to a prompt; this is where it lands.
    /// </summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>The provider-qualified model id that produced the draft.</summary>
    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the note was last saved.
    /// </summary>
    /// <remarks>
    /// The first such column in the project — every other entity carries only <c>CreatedAt</c>,
    /// because nothing else was editable after insert. Re-saving from <c>/notes/{id}</c> is what
    /// requires it. Noted here as a deliberate precedent rather than an oversight: if another
    /// entity ever needs one, it should be named and stamped the same way.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; }
}
