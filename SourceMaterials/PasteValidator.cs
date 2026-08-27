using _10xnotes.Text;
namespace _10xnotes.SourceMaterials;

/// <summary>
/// Every rule that decides whether pasted text becomes a
/// <see cref="Data.Entities.SourceMaterial"/>, and what it is called.
/// </summary>
/// <remarks>
/// Pure by design: no EF, no components, no ASP.NET — the same split
/// <see cref="MarkdownImportValidator"/> uses for the file path, and for the same reason. The
/// project has no bUnit, so anything left inside the page cannot be tested at all; the page holds
/// nothing beyond mapping a <see cref="PasteFailure"/> onto Polish copy.
/// </remarks>
public static class PasteValidator
{
    /// <summary>
    /// The upper bound on pasted text, counted in <em>UTF-16 code units</em>.
    /// </summary>
    /// <remarks>
    /// The same number as <see cref="MarkdownImportValidator.MaxFileSizeBytes"/> and deliberately
    /// <em>not</em> the same unit — that one counts bytes. 128 KB of Polish UTF-8 is roughly 64 K
    /// characters, so a paste at this limit is about twice the text an import at its limit
    /// carries. That is the intent: a user who could have imported a document should be able to
    /// paste its contents instead, and matching the units would have made the paste path the
    /// stingier of the two for exactly the users the app is written for.
    /// <para>
    /// So this is a decision, not an oversight. Do not "fix" the two constants into agreement
    /// without also deciding which unit the user is being promised.
    /// </para>
    /// <para>
    /// It is also the larger of the two limits crossing the SignalR boundary, which is why
    /// <c>HubWireLimits</c> derives the transport bound from it.
    /// </para>
    /// <para>
    /// The last layer beneath this number is the model's context window, and it is the one layer
    /// with no check of its own. <c>MarkdownImportValidator.MaxFileSizeBytes</c> justifies its
    /// value by keeping an imported document comfortably inside that window; counted in characters
    /// this limit carries two to three times the bytes at the same nominal number, and the whole
    /// material goes into the prompt. It degrades rather than breaks — <c>NoteGenerator</c> maps
    /// the provider's 413 and context-length 400s to <c>GenerationFailure.TooLong</c> — but
    /// <c>GenerationQuotaService</c> reserves the daily slot *before* the provider call, so an
    /// over-long material costs the user a generation for nothing. The default model has ample
    /// context; if <c>Ai__Model</c> is ever pointed at a smaller one, this number is what has to
    /// be re-checked against it.
    /// </para>
    /// </remarks>
    public const int MaxContentLength = 128 * 1024;

    /// <summary>
    /// Fallback for a paste whose first line yields nothing usable; <c>Title</c> is required.
    /// </summary>
    /// <remarks>
    /// The same wording as <see cref="MarkdownImportValidator.FallbackTitle"/>: the user cannot
    /// tell which entry point produced a nameless material, so neither should the copy.
    /// </remarks>
    public const string FallbackTitle = MarkdownImportValidator.FallbackTitle;

    /// <summary>Matches the <c>title</c> column width.</summary>
    private const int MaxTitleLength = 200;

    /// <summary>
    /// The title proposed to the user when they paste. They can correct it before saving, so this
    /// only has to be a reasonable default — never empty, and never over the column width.
    /// </summary>
    /// <remarks>
    /// The first non-blank line, with any Markdown heading marker stripped: pasted material
    /// usually opens with its own title, and <c>## Wprowadzenie</c> is a worse suggestion than
    /// <c>Wprowadzenie</c>. Only the leading <c>#</c> run is removed — a <c>#</c> in the middle of
    /// a line is part of the text.
    /// </remarks>
    public static string DeriveTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return FallbackTitle;
        }

        // Split on '\n' and trim the line, which handles CRLF without a separate case: the '\r'
        // is trailing whitespace on the line it ends.
        foreach (var line in content.Split('\n'))
        {
            var title = line.TrimStart().TrimStart('#').Trim();

            if (title.Length == 0)
            {
                continue;
            }

            return TextLimits.Truncate(title, MaxTitleLength).TrimEnd();
        }

        return FallbackTitle;
    }

    /// <summary>
    /// Runs the paste rules in order and returns the text or a typed rejection.
    /// </summary>
    /// <remarks>
    /// Empty before too-long, matching <see cref="MarkdownImportValidator.Validate"/>, so the user
    /// is told the most specific thing that is wrong.
    /// <para>
    /// The length check runs here regardless of the <c>maxlength</c> on the textarea. That
    /// attribute is a convenience for the user, not a control: it is trivially removed, and a
    /// value past the limit does not fail politely — it exceeds the hub's receive bound and aborts
    /// the circuit, which is the one failure the page cannot show a message for.
    /// </para>
    /// </remarks>
    public static PasteResult Validate(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return PasteResult.Failure(PasteFailure.Empty);
        }

        if (content.Length > MaxContentLength)
        {
            return PasteResult.Failure(PasteFailure.TooLong);
        }

        // Not trimmed, unlike the title: leading whitespace is significant in Markdown — it is
        // what makes an indented code block a code block — so the text is stored exactly as the
        // user pasted it, the same posture NoteValidator.Validate takes on a note body.
        return PasteResult.Success(content);
    }
}
