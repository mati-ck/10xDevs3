namespace _10xnotes.Notes;

/// <summary>
/// One row of the notes list: exactly what the list shows, and nothing else.
/// </summary>
/// <remarks>
/// The first projection type in the project — every read before this one returned a whole entity.
/// A deliberate precedent rather than a local optimisation: <c>Note.Content</c> and
/// <c>Note.DraftContent</c> are 64 KB each, so a list of twenty notes read as entities drags
/// megabytes of text into memory in order to render a title and a date.
/// <para>
/// The material's title is deliberately absent. <c>NoteValidator.DeriveTitle</c> seeds a note's
/// title from its material's, so the two strings are nearly always identical — the row carries
/// <see cref="SourceMaterialId"/> and links to it under a fixed caption instead of printing the
/// same words twice.
/// </para>
/// </remarks>
/// <param name="Id">The note's key, and the target of <c>/notes/{id}</c>.</param>
/// <param name="Title">The title the user saved.</param>
/// <param name="UpdatedAt">When the note was last saved — what the list sorts by.</param>
/// <param name="SourceMaterialId">The material this note was generated from.</param>
public sealed record NoteListItem(Guid Id, string Title, DateTimeOffset UpdatedAt, Guid SourceMaterialId);
