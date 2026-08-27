using _10xnotes.SourceMaterials;

namespace _10xnotes.Notes;

/// <summary>
/// Every rule that decides whether an edited note can be saved, and what it is called.
/// </summary>
/// <remarks>
/// Pure by design: no EF, no components, no ASP.NET. The editor and <c>NoteService</c> hold no
/// rules of their own beyond mapping a <see cref="NoteValidationFailure"/> onto Polish copy —
/// the same split <c>MarkdownImportValidator</c> uses for import, and the only thing that makes
/// these branches testable in a project with no bUnit.
/// </remarks>
public static class NoteValidator
{
    /// <summary>Matches the <c>title</c> column width, which matches <c>SourceMaterial.Title</c>.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// The upper bound on a note body, enforced in application code rather than by the column.
    /// </summary>
    /// <remarks>
    /// Deliberately lower than the 128 KB import limit. A note is a summary of a material, so it
    /// is expected to be substantially shorter than its source — but the field is user-editable,
    /// which makes it a lever for unbounded row growth without a bound of its own. The column
    /// stays an unbounded Postgres <c>text</c>, following <c>SourceMaterial.Content</c>.
    /// </remarks>
    public const int MaxContentLength = 64 * 1024;

    /// <summary>Fallback for a material whose title yields nothing usable; the title is required.</summary>
    public const string FallbackTitle = "Notatka";

    /// <summary>
    /// The title proposed when a freshly generated note enters the editor. The user can correct
    /// it before saving, so this only has to be a reasonable default — never empty, and never
    /// over the column width.
    /// </summary>
    public static string DeriveTitle(string? materialTitle)
    {
        var title = (materialTitle ?? string.Empty).Trim();

        if (title.Length == 0)
        {
            return FallbackTitle;
        }

        return TextLimits.Truncate(title, MaxTitleLength).TrimEnd();
    }

    /// <summary>
    /// Runs the save rules in order and returns the normalized note or a typed rejection.
    /// </summary>
    /// <remarks>
    /// Title before content, and empty before too-long, so the user is told the most specific
    /// thing that is wrong rather than whichever check happens to run first.
    /// </remarks>
    public static NoteValidationResult Validate(string? title, string? content)
    {
        var trimmedTitle = (title ?? string.Empty).Trim();

        if (trimmedTitle.Length == 0)
        {
            return NoteValidationResult.Failure(NoteValidationFailure.TitleEmpty);
        }

        if (trimmedTitle.Length > MaxTitleLength)
        {
            return NoteValidationResult.Failure(NoteValidationFailure.TitleTooLong);
        }

        // Not trimmed, unlike the title: leading whitespace is significant in Markdown — it is
        // what makes an indented code block a code block — so the body is stored exactly as the
        // user left it, and only *emptiness* is judged whitespace-insensitively.
        var body = content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(body))
        {
            return NoteValidationResult.Failure(NoteValidationFailure.ContentEmpty);
        }

        if (body.Length > MaxContentLength)
        {
            return NoteValidationResult.Failure(NoteValidationFailure.ContentTooLong);
        }

        return NoteValidationResult.Success(trimmedTitle, body);
    }
}
