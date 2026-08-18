using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace _10xnotes.Notes;

/// <summary>
/// Turns a note's Markdown into the HTML shown in the editor's preview.
/// </summary>
/// <remarks>
/// This is the only place in the project where content becomes markup — the preview renders the
/// result through <c>MarkupString</c>, which bypasses Blazor's escaping entirely. Everything else
/// (the source material, the streaming panel) is deliberately shown as text. So the safety of
/// this method is the safety of that decision, and it is pinned by <c>NoteMarkdownTests</c>
/// rather than by manual clicking.
/// <para>
/// Two layers, both required. <see cref="MarkdownPipelineBuilder.DisableHtml"/> removes the HTML
/// block parser and inline HTML parsing, so <c>&lt;script&gt;</c> in the note comes out as text.
/// Markdig's own documentation says that is not by itself a security solution and points at link
/// destinations — after HTML is off, <c>href</c>, <c>src</c> and <c>title</c> are the only
/// attributes content still controls, so a scheme allowlist closes what is left.
/// </para>
/// </remarks>
public static class NoteMarkdown
{
    /// <summary>
    /// Built once and shared. A built <see cref="MarkdownPipeline"/> is immutable and safe to use
    /// from any thread — rebuilding it per render would repeat the extension setup for every
    /// keystroke the preview repaints on.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    /// <summary>
    /// The schemes a note may link to. Everything else — <c>javascript:</c>, <c>data:</c>,
    /// <c>vbscript:</c>, and anything a future browser adds — is neutralized rather than blocked
    /// by name, so the list cannot go stale in the dangerous direction.
    /// </summary>
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    /// <summary>
    /// What a rejected destination is replaced with. A bare fragment rather than an empty string:
    /// both are inert, but an empty <c>src</c> makes some browsers re-request the current page.
    /// </summary>
    private const string NeutralizedUrl = "#";

    /// <summary>
    /// Renders note Markdown to HTML safe to hand to <c>MarkupString</c>.
    /// </summary>
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        // Parsed and rendered as two steps rather than through Markdown.ToHtml, because the
        // filtering below happens on the document in between. The same pipeline instance must be
        // passed to both calls, or the renderer runs without the extensions the parser used.
        var document = Markdown.Parse(markdown, Pipeline);

        NeutralizeUnsafeUrls(document);

        return document.ToHtml(Pipeline);
    }

    /// <summary>
    /// Rewrites every destination in <paramref name="document"/> that the scheme allowlist does
    /// not permit, so the rendered HTML can carry no executable address.
    /// </summary>
    /// <remarks>
    /// Public, and separate from <see cref="ToHtml"/>, so this property can be tested against a
    /// document whose nodes have been tampered with the way a Markdig extension would tamper with
    /// them — <see cref="ToHtml"/> parses internally, so a test has no way to reach a node between
    /// parsing and rendering. Given the project has no bUnit, a security guarantee that cannot be
    /// reached from a test is a security guarantee nobody is holding.
    /// </remarks>
    public static void NeutralizeUnsafeUrls(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Covers both links and images: Markdig models an image as a LinkInline with IsImage set,
        // so one pass reaches every href and every src in the document.
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Cleared unconditionally, and before the check below rather than after it. The HTML
            // renderer prefers GetDynamicUrl over Url, so a node carrying one renders that
            // address no matter what Url is set to — neutralizing Url alone would be a no-op.
            // No extension under the current pipeline sets it, which is exactly the problem:
            // adding UseAdvancedExtensions, UseMediaLinks, UseJiraLinks or UseAutoLinks would
            // silently reopen the hole this class exists to close.
            link.GetDynamicUrl = null;

            // An empty destination is rewritten too, not just an unsafe one. `![x]()` is legal
            // Markdown and renders `src=""`, which is the case NeutralizedUrl was chosen to avoid
            // in the first place — some browsers resolve an empty src against the current
            // document and re-request the page. Inert either way; this just makes the code do
            // what the constant above says it does.
            if (string.IsNullOrEmpty(link.Url) || !IsSafe(link.Url))
            {
                link.Url = NeutralizedUrl;
            }
        }

        // Autolinks are a separate node type and are easy to forget: `<javascript:alert(1)>` is a
        // well-formed CommonMark autolink, so it never passes through LinkInline at all.
        //
        // The renderer uses Url for the visible label as well, so neutralizing one also blanks the
        // other and the reader sees "#" rather than the address they wrote. Accepted rather than
        // worked around: the only destinations that reach this branch are ones no note has a
        // legitimate reason to carry, and preserving the text would mean rebuilding the node.
        foreach (var autolink in document.Descendants<AutolinkInline>())
        {
            if (!IsSafe(autolink.Url))
            {
                autolink.Url = NeutralizedUrl;
            }
        }
    }

    private static bool IsSafe(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        var scheme = SchemeOf(url);

        // No scheme means a relative path or an in-page anchor. Neither can execute anything, and
        // both are legitimate in a note the user writes.
        return scheme is null
            || AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The scheme a browser would resolve from this destination, or <c>null</c> when it carries
    /// none.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Uri"/>: a relative destination is not a parseable absolute URI, so going
    /// through Uri would mean treating "failed to parse" as "safe", which is the wrong default in
    /// the one place where the default matters.
    /// <para>
    /// Whitespace and control characters are dropped rather than rejected because browsers strip
    /// them before resolving the scheme — <c>java&#92;nscript:alert(1)</c> is a <c>javascript:</c>
    /// URL to them, and a naive <c>StartsWith</c> check would wave it through. Entity references
    /// need no handling here: CommonMark decodes them inside link destinations, so Markdig has
    /// already resolved <c>&amp;#106;avascript:</c> into plain text by the time this runs.
    /// </para>
    /// </remarks>
    private static string? SchemeOf(string url)
    {
        var scheme = new StringBuilder();

        foreach (var character in url)
        {
            if (character <= ' ' || character == '\u007f')
            {
                continue;
            }

            if (character == ':')
            {
                return scheme.ToString();
            }

            // A path, query or fragment separator reached before any colon means the destination
            // is relative — "notes/2026:03.md" is a filename, not a "notes/2026" scheme.
            if (character is '/' or '?' or '#')
            {
                return null;
            }

            scheme.Append(character);
        }

        return null;
    }
}
