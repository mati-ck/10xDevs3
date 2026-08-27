namespace _10xnotes.Data.Entities;

/// <summary>
/// Text the user supplied — an imported Markdown file or a paste — kept as the source of
/// truth a note is generated from.
/// </summary>
/// <remarks>
/// The first domain entity in the project. It follows the ownership convention
/// <see cref="Profile"/> established — surrogate <see cref="Id"/> plus a separate
/// <see cref="OwnerId"/> — so isolation comes from implementing <see cref="IOwnedByUser"/>
/// and nothing else.
/// <para>
/// Unlike <see cref="Profile"/>, <see cref="OwnerId"/> is deliberately NOT unique here: one
/// user owns many materials, and re-importing the same file is a legitimate act rather than
/// a duplicate to reject.
/// </para>
/// </remarks>
public sealed class SourceMaterial : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>auth.users.id</c>; not unique — a user owns many materials.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Derived from the file name at import and correctable by the user before saving.
    /// Required, so the material is never nameless in a list.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The decoded UTF-8 text of the imported file, stored verbatim.
    /// <para>
    /// Unbounded at the database level (Postgres <c>text</c>); the real bound is the import
    /// size limit enforced in application code, which keeps this from being a lever for
    /// unbounded row growth.
    /// </para>
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Where this material came from. Read it rather than inferring provenance from
    /// <see cref="OriginalFileName"/> being absent.
    /// </summary>
    public SourceMaterialKind Kind { get; set; }

    /// <summary>
    /// The file name as uploaded. Kept alongside <see cref="Title"/> so editing the title
    /// never costs the user the provenance of where the material came from.
    /// <para>
    /// <c>null</c> means "this material did not come from a file" — a paste — not "the name
    /// could not be read". <see cref="Kind"/> is what states that; this property only carries
    /// the name when there is one.
    /// </para>
    /// </summary>
    public string? OriginalFileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
