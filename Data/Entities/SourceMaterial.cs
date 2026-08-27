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
    /// Empty for a paste, and required rather than nullable — deliberately. Nullability looked
    /// like the honest model ("no file behind this row"), but <see cref="Kind"/> already states
    /// that, so the column would have carried the same fact twice while costing rollback safety:
    /// the application version before <c>Kind</c> existed maps this as required, and EF throws
    /// when it materialises a null into a required property. A Coolify rollback reverts code and
    /// not schema, so a null here would have broken the material page for the version rolled back
    /// to. Read <see cref="Kind"/> for provenance; never infer it from this being empty.
    /// </para>
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
