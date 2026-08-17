using _10xnotes.Notes;

namespace _10xNotes.Tests;

/// <summary>
/// The note preview is the only place in the project where content becomes markup — it goes
/// through <c>MarkupString</c>, which bypasses Blazor's escaping. Both properties that make that
/// acceptable are ordinary string assertions, so they are pinned here rather than left to
/// somebody remembering to paste a script tag into the editor.
/// </summary>
/// <remarks>
/// The blast radius is a note visible only to its owner, so the surface is self-XSS rather than
/// somebody else's session. That is a reason for these tests to exist, not a reason to skip them:
/// the sharing this product does not have yet is exactly what would turn one into the other.
/// </remarks>
public sealed class NoteMarkdownTests
{
    // -- Structure ------------------------------------------------------------------------

    [Fact]
    public void Headings_become_heading_elements()
    {
        // The inherited finding this slice closes: the prompt asks for Markdown headings and
        // bullets, and until now the panel showed the user a literal "##".
        var html = NoteMarkdown.ToHtml("## Streszczenie");

        Assert.Contains("<h2", html);
        Assert.Contains("Streszczenie", html);
        Assert.DoesNotContain("##", html);
    }

    [Fact]
    public void Bullets_become_list_items()
    {
        var html = NoteMarkdown.ToHtml("- pierwszy\n- drugi");

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>pierwszy</li>", html);
        Assert.Contains("<li>drugi</li>", html);
    }

    [Fact]
    public void Empty_input_renders_nothing()
    {
        Assert.Equal(string.Empty, NoteMarkdown.ToHtml(string.Empty));
        Assert.Equal(string.Empty, NoteMarkdown.ToHtml(null));
    }

    // -- Raw HTML -------------------------------------------------------------------------

    [Fact]
    public void A_script_tag_in_the_note_does_not_survive_as_a_tag()
    {
        var html = NoteMarkdown.ToHtml("<script>alert(1)</script>");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        // Escaped, not dropped: the user typed it, so they should see it as text rather than
        // watch part of their note disappear.
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void An_event_handler_attribute_does_not_survive_as_markup()
    {
        var html = NoteMarkdown.ToHtml("<img src=x onerror=alert(1)>");

        // The handler name survives as *text* — what matters is that no tag opens, so it can
        // never be an attribute. Asserting the word is absent would be asserting the wrong thing:
        // a note that discusses onerror handlers is a legitimate note.
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
    }

    [Fact]
    public void An_html_block_does_not_survive_as_markup()
    {
        var html = NoteMarkdown.ToHtml("<div onclick=\"alert(1)\">\n\ntreść\n\n</div>");

        Assert.DoesNotContain("<div", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</div>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;div onclick=", html);
    }

    // -- Link schemes ---------------------------------------------------------------------

    [Theory]
    [InlineData("[klik](javascript:alert(1))")]
    [InlineData("[klik](JavaScript:alert(1))")]
    [InlineData("[klik](  javascript:alert(1))")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("![obraz](javascript:alert(1))")]
    [InlineData("[klik](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("[klik](vbscript:msgbox(1))")]
    public void A_dangerous_scheme_is_neutralized(string markdown)
    {
        var html = NoteMarkdown.ToHtml(markdown);

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[klik](<java script:alert(1)>)")]
    [InlineData("[klik](<java\tscript:alert(1)>)")]
    public void A_scheme_split_by_whitespace_is_neutralized(string markdown)
    {
        // Only an angle-bracket destination can carry whitespace — a bare "(java script:…)" is
        // not a link at all, so this is the form the check has to survive. Browsers strip
        // whitespace and control characters out of a URL before resolving its scheme, so to them
        // this is a javascript: URL. A StartsWith on the raw string would wave it through, which
        // is why the scheme is read character by character.
        var html = NoteMarkdown.ToHtml(markdown);

        var collapsed = html.Replace(" ", string.Empty).Replace("\t", string.Empty);

        Assert.DoesNotContain("javascript:", collapsed, StringComparison.OrdinalIgnoreCase);
        // The escaped forms a browser also strips before resolving the scheme.
        Assert.DoesNotContain("%20", html);
        Assert.DoesNotContain("&#9;", html);
    }

    [Theory]
    [InlineData("[klik](https://example.com)", "https://example.com")]
    [InlineData("[klik](http://example.com/a?b=c)", "http://example.com/a?b=c")]
    [InlineData("[napisz](mailto:kto@example.com)", "mailto:kto@example.com")]
    public void An_allowed_scheme_passes_through_untouched(string markdown, string expectedUrl)
    {
        var html = NoteMarkdown.ToHtml(markdown);

        Assert.Contains($"href=\"{expectedUrl}\"", html);
    }

    [Theory]
    [InlineData("[sekcja](#streszczenie)", "#streszczenie")]
    [InlineData("[plik](notatki/wyklad.md)", "notatki/wyklad.md")]
    [InlineData("[katalog](/materials/import)", "/materials/import")]
    public void A_destination_with_no_scheme_passes_through(string markdown, string expectedUrl)
    {
        // Relative paths and in-page anchors carry no scheme, so they cannot execute anything —
        // and a note that links to its own headings is exactly what an outline is for.
        var html = NoteMarkdown.ToHtml(markdown);

        Assert.Contains($"href=\"{expectedUrl}\"", html);
    }

    [Fact]
    public void A_colon_inside_a_relative_path_is_not_read_as_a_scheme()
    {
        var html = NoteMarkdown.ToHtml("[plik](notatki/2026:03.md)");

        Assert.Contains("href=\"notatki/2026:03.md\"", html);
    }

    [Fact]
    public void An_https_image_passes_through()
    {
        var html = NoteMarkdown.ToHtml("![diagram](https://example.com/a.png)");

        Assert.Contains("src=\"https://example.com/a.png\"", html);
    }

    [Fact]
    public void An_email_autolink_passes_through()
    {
        var html = NoteMarkdown.ToHtml("<kto@example.com>");

        Assert.Contains("mailto:kto@example.com", html);
    }
}
