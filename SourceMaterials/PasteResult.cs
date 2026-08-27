namespace _10xnotes.SourceMaterials;

/// <summary>Why a paste was rejected, in terms the UI can act on.</summary>
/// <remarks>
/// Exactly two reasons, and deliberately not four: the extension and UTF-8 checks that
/// <see cref="MarkdownImportFailure"/> carries cannot fail for a value that arrives already a
/// <see cref="string"/>. Copying them across for symmetry would put branches in the page that
/// nothing can ever reach.
/// </remarks>
public enum PasteFailure
{
    None = 0,

    /// <summary>Nothing was pasted, or nothing but whitespace.</summary>
    Empty,

    /// <summary>The text exceeds <see cref="PasteValidator.MaxContentLength"/>.</summary>
    TooLong
}

/// <summary>
/// Outcome of validating pasted text.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MarkdownImportResult"/>: a classified reason rather than a message, because
/// the reason is a fact about the text while the wording is a UI concern, and the PRD requires
/// Polish copy. One enum member per user-visible message, with no catch-all.
/// </remarks>
public sealed record PasteResult
{
    private PasteResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>
    /// The text exactly as pasted. Empty unless <see cref="Succeeded"/>.
    /// </summary>
    /// <remarks>
    /// Verbatim — not trimmed, no BOM stripped. Leading whitespace is significant in Markdown, and
    /// the clipboard does not carry a BOM the way a file's bytes do.
    /// </remarks>
    public string Content { get; private init; } = string.Empty;

    public PasteFailure FailureReason { get; private init; }

    public static PasteResult Success(string content) =>
        new() { Succeeded = true, Content = content };

    public static PasteResult Failure(PasteFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
