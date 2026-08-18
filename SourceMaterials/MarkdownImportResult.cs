namespace _10xnotes.SourceMaterials;

/// <summary>Why an import was rejected, in terms the UI can act on.</summary>
public enum MarkdownImportFailure
{
    None = 0,

    /// <summary>The file is not a Markdown file by extension.</summary>
    UnsupportedExtension,

    /// <summary>The file exceeds <see cref="MarkdownImportValidator.MaxFileSizeBytes"/>.</summary>
    TooLarge,

    /// <summary>The bytes are not valid UTF-8 — a binary file, or a legacy encoding.</summary>
    NotValidUtf8,

    /// <summary>The file decoded cleanly but carries no actual text.</summary>
    Empty
}

/// <summary>
/// Outcome of validating an uploaded file.
/// </summary>
/// <remarks>
/// Mirrors <c>AuthResult</c>: a classified reason rather than a message, because the reason is
/// a fact about the file while the wording is a UI concern, and the PRD requires Polish copy.
/// One enum member per user-visible message, with no catch-all — a rejection the user cannot
/// act on is worse than no validation at all.
/// </remarks>
public sealed record MarkdownImportResult
{
    private MarkdownImportResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>The decoded text, BOM stripped. Empty unless <see cref="Succeeded"/>.</summary>
    public string Content { get; private init; } = string.Empty;

    public MarkdownImportFailure FailureReason { get; private init; }

    public static MarkdownImportResult Success(string content) =>
        new() { Succeeded = true, Content = content };

    public static MarkdownImportResult Failure(MarkdownImportFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
