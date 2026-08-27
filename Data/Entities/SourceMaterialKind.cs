namespace _10xnotes.Data.Entities;

/// <summary>
/// Where a <see cref="SourceMaterial"/> came from.
/// </summary>
/// <remarks>
/// Provenance is named explicitly rather than inferred from whether
/// <see cref="SourceMaterial.OriginalFileName"/> happens to be null. An absent file name is
/// evidence of two different things — "this did not come from a file" and "the name could not
/// be read" — and only one of them should change what the detail page says.
/// <para>
/// Persisted as text, like <see cref="NoteEventKind"/>, for the same reason: the only consumer
/// beyond the application is a SQL query somebody writes by hand, where
/// <c>where kind = 'Paste'</c> is legible and <c>where kind = 1</c> is not.
/// </para>
/// </remarks>
public enum SourceMaterialKind
{
    /// <summary>An uploaded Markdown file. The zero value, because it is what every row predating this enum is.</summary>
    MarkdownFile = 0,

    /// <summary>Text pasted straight into the page — no file, and so no file name.</summary>
    Paste = 1
}
