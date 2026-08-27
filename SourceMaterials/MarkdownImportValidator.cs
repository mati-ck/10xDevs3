using System.Text;

namespace _10xnotes.SourceMaterials;

/// <summary>
/// Every rule that decides whether an uploaded file becomes a
/// <see cref="Data.Entities.SourceMaterial"/>, and what it is called.
/// </summary>
/// <remarks>
/// Pure by design: no <c>IBrowserFile</c>, no EF, no ASP.NET. The upload page holds no rules of
/// its own beyond mapping a <see cref="MarkdownImportFailure"/> onto Polish copy, which is what
/// lets these branches — the ones most likely to be wrong — be unit-tested without a browser,
/// a database, or a component harness.
/// </remarks>
public static class MarkdownImportValidator
{
    /// <summary>
    /// The single source of truth for the import size limit, referenced both by the page's
    /// pre-check and by its <c>OpenReadStream</c> argument so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// 128 KB is roughly 35 pages of dense text. The bound is deliberate on two fronts: it keeps
    /// the whole file in memory safely, and it keeps any imported document comfortably inside an
    /// LLM context window, so the generation slice never has to chunk or truncate.
    /// </remarks>
    public const long MaxFileSizeBytes = 128 * 1024;

    /// <summary>Fallback for a file whose name yields nothing usable; <c>Title</c> is required.</summary>
    public const string FallbackTitle = "Materiał źródłowy";

    /// <summary>Matches the <c>original_file_name</c> column width.</summary>
    private const int MaxFileNameLength = 260;

    /// <summary>Matches the <c>title</c> column width.</summary>
    private const int MaxTitleLength = 200;

    private static readonly string[] AcceptedExtensions = [".md", ".markdown"];

    /// <summary>
    /// Throws on invalid UTF-8 rather than substituting U+FFFD. The default <see cref="Encoding.UTF8"/>
    /// is lenient, so a binary or legacy-encoded file would decode "successfully" into replacement
    /// characters and be stored as garbage — surfacing much later as nonsense generated output,
    /// far from its cause.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Whether the file claims to be Markdown. Extension only — the content check is separate.</summary>
    public static bool IsAcceptedExtension(string? fileName)
    {
        var extension = Path.GetExtension(SanitizeFileName(fileName));

        return AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The title proposed to the user when they pick a file. They can correct it before saving,
    /// so this only has to be a reasonable default — never empty, and never over the column width.
    /// </summary>
    public static string DeriveTitle(string? fileName)
    {
        var title = Path.GetFileNameWithoutExtension(SanitizeFileName(fileName)).Trim();

        if (title.Length == 0)
        {
            return FallbackTitle;
        }

        return TextLimits.Truncate(title, MaxTitleLength).TrimEnd();
    }

    /// <summary>
    /// Strips any directory part and clamps to the column width.
    /// </summary>
    /// <remarks>
    /// The browser sends a bare name, so this is belt-and-braces — but an over-long name would
    /// otherwise fail at <c>SaveChanges</c> with a database error rather than a message the user
    /// can act on. Both separators are handled explicitly: <see cref="Path.GetFileName(string)"/>
    /// does not treat a backslash as a separator on Linux, where this runs in production.
    /// </remarks>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..].Trim();

        return TextLimits.Truncate(name, MaxFileNameLength);
    }

    /// <summary>
    /// Runs the import rules in order and returns the decoded text or a typed rejection.
    /// </summary>
    /// <remarks>
    /// Order matters: cheap and specific first, so the user gets the most useful message. A
    /// renamed PDF should be told it is not readable text, not that it is too large.
    /// </remarks>
    public static MarkdownImportResult Validate(byte[] content, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!IsAcceptedExtension(fileName))
        {
            return MarkdownImportResult.Failure(MarkdownImportFailure.UnsupportedExtension);
        }

        if (content.LongLength > MaxFileSizeBytes)
        {
            return MarkdownImportResult.Failure(MarkdownImportFailure.TooLarge);
        }

        string text;

        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return MarkdownImportResult.Failure(MarkdownImportFailure.NotValidUtf8);
        }

        // A BOM survives decoding as a leading U+FEFF. Left in place it would show up at the top
        // of the detail view and, later, at the top of the generation prompt.
        // Escaped rather than literal: a raw U+FEFF in source is invisible and would be lost to
        // the first well-meaning editor that normalizes the file.
        text = text.TrimStart('\uFEFF');

        return string.IsNullOrWhiteSpace(text)
            ? MarkdownImportResult.Failure(MarkdownImportFailure.Empty)
            : MarkdownImportResult.Success(text);
    }
}
