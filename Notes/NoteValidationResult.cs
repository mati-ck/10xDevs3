namespace _10xnotes.Notes;

/// <summary>Why a note cannot be saved, in terms the UI can act on.</summary>
public enum NoteValidationFailure
{
    None = 0,

    /// <summary>The title is blank; a note is never nameless.</summary>
    TitleEmpty,

    /// <summary>The title exceeds <see cref="NoteValidator.MaxTitleLength"/>.</summary>
    TitleTooLong,

    /// <summary>The note carries no actual text — saving it would accept nothing.</summary>
    ContentEmpty,

    /// <summary>The note exceeds <see cref="NoteValidator.MaxContentLength"/>.</summary>
    ContentTooLong
}

/// <summary>
/// Outcome of validating a note the user is about to save.
/// </summary>
/// <remarks>
/// Mirrors <c>MarkdownImportResult</c>: a classified reason rather than a message, because the
/// reason is a fact about the note while the wording is a UI concern, and the PRD requires Polish
/// copy. One enum member per user-visible message, with no catch-all.
/// <para>
/// Success carries the normalized values rather than only a flag, so the caller saves exactly
/// what was validated. Without that the page could validate a trimmed title and then persist the
/// untrimmed one, which is how a length check silently stops matching the column width.
/// </para>
/// </remarks>
public sealed record NoteValidationResult
{
    private NoteValidationResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>The trimmed title. Empty unless <see cref="Succeeded"/>.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>The note body, verbatim. Empty unless <see cref="Succeeded"/>.</summary>
    public string Content { get; private init; } = string.Empty;

    public NoteValidationFailure FailureReason { get; private init; }

    public static NoteValidationResult Success(string title, string content) =>
        new() { Succeeded = true, Title = title, Content = content };

    public static NoteValidationResult Failure(NoteValidationFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
