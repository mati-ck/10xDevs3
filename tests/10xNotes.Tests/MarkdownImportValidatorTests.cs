using System.Text;
using _10xnotes.SourceMaterials;

namespace _10xNotes.Tests;

/// <summary>
/// Pins every branch of the import rules. These decide what reaches the database and, in the
/// next slice, what reaches the AI prompt — so a wrong branch here is not a UI annoyance but
/// stored garbage that surfaces far from its cause.
/// </summary>
public sealed class MarkdownImportValidatorTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static byte[] Utf8(string text) => StrictUtf8.GetBytes(text);

    // -- Extension gate -------------------------------------------------------------------

    [Theory]
    [InlineData("wyklad.md")]
    [InlineData("wyklad.markdown")]
    [InlineData("WYKLAD.MD")]
    [InlineData("Wyklad.MarkDown")]
    [InlineData("notatki.z.wykladu.md")]
    public void Markdown_extensions_are_accepted(string fileName) =>
        Assert.True(MarkdownImportValidator.IsAcceptedExtension(fileName));

    [Theory]
    [InlineData("wyklad.txt")]
    [InlineData("wyklad.pdf")]
    [InlineData("wyklad")]
    [InlineData("wyklad.md.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_rejected(string? fileName) =>
        Assert.False(MarkdownImportValidator.IsAcceptedExtension(fileName));

    [Fact]
    public void A_non_markdown_file_is_rejected_by_extension()
    {
        var result = MarkdownImportValidator.Validate(Utf8("# Tresc"), "wyklad.txt");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.UnsupportedExtension, result.FailureReason);
    }

    // -- Size bound -----------------------------------------------------------------------

    [Fact]
    public void A_file_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new string('a', (int)MarkdownImportValidator.MaxFileSizeBytes);

        var result = MarkdownImportValidator.Validate(Utf8(atLimit), "wyklad.md");

        Assert.True(result.Succeeded);
        Assert.Equal(atLimit.Length, result.Content.Length);
    }

    [Fact]
    public void One_byte_over_the_limit_is_rejected()
    {
        var overLimit = new string('a', (int)MarkdownImportValidator.MaxFileSizeBytes + 1);

        var result = MarkdownImportValidator.Validate(Utf8(overLimit), "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.TooLarge, result.FailureReason);
    }

    [Fact]
    public void The_limit_counts_bytes_not_characters()
    {
        // Polish diacritics are two bytes each in UTF-8, so a string of half the limit in
        // characters is exactly at the limit in bytes — and one more character puts it over.
        // Measuring the string length instead of the byte length would let this through.
        var justOver = new string('ą', (int)(MarkdownImportValidator.MaxFileSizeBytes / 2) + 1);

        var result = MarkdownImportValidator.Validate(Utf8(justOver), "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.TooLarge, result.FailureReason);
    }

    // -- Encoding -------------------------------------------------------------------------

    [Fact]
    public void Invalid_utf8_is_rejected_rather_than_silently_mangled()
    {
        // A lone 0xFF never appears in valid UTF-8. With the framework's lenient default this
        // would decode to U+FFFD and be stored as garbage instead of failing.
        byte[] notUtf8 = [0x23, 0x20, 0xFF, 0xFE, 0x00, 0x41];

        var result = MarkdownImportValidator.Validate(notUtf8, "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.NotValidUtf8, result.FailureReason);
    }

    [Fact]
    public void Polish_diacritics_survive_the_round_trip()
    {
        const string content = "# Wykład\n\nZażółć gęślą jaźń — *kursywa* i `kod`.\n";

        var result = MarkdownImportValidator.Validate(Utf8(content), "wyklad.md");

        Assert.True(result.Succeeded);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public void A_byte_order_mark_is_stripped()
    {
        // Editors on Windows commonly write one. Left in place it renders as a stray glyph at
        // the top of the material and would lead the generation prompt in the next slice.
        byte[] withBom = [.. StrictUtf8.GetPreamble().Concat(Utf8("# Wykład"))];

        var result = MarkdownImportValidator.Validate(withBom, "wyklad.md");

        Assert.True(result.Succeeded);
        Assert.Equal("# Wykład", result.Content);
    }

    [Fact]
    public void Content_is_stored_verbatim_apart_from_the_bom()
    {
        // The PRD guardrail is that the source stays unchanged, so trailing whitespace and
        // blank lines must not be tidied away.
        const string content = "\n\n#   Wykład   \n\n\ntresc\n\n   ";

        var result = MarkdownImportValidator.Validate(Utf8(content), "wyklad.md");

        Assert.True(result.Succeeded);
        Assert.Equal(content, result.Content);
    }

    // -- Emptiness ------------------------------------------------------------------------

    [Fact]
    public void A_zero_byte_file_is_rejected()
    {
        var result = MarkdownImportValidator.Validate([], "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.Empty, result.FailureReason);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\n\n\n")]
    [InlineData(" \t\r\n ")]
    public void A_whitespace_only_file_is_rejected(string content)
    {
        var result = MarkdownImportValidator.Validate(Utf8(content), "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.Empty, result.FailureReason);
    }

    [Fact]
    public void A_file_holding_only_a_bom_is_rejected_as_empty()
    {
        var result = MarkdownImportValidator.Validate(StrictUtf8.GetPreamble(), "wyklad.md");

        Assert.False(result.Succeeded);
        Assert.Equal(MarkdownImportFailure.Empty, result.FailureReason);
    }

    // -- Rule ordering --------------------------------------------------------------------

    [Fact]
    public void The_extension_is_checked_before_the_size()
    {
        // A user who picked the wrong file should be told it is the wrong kind of file, which
        // is actionable, rather than that it is too large, which sends them off to split it.
        var oversized = Utf8(new string('a', (int)MarkdownImportValidator.MaxFileSizeBytes + 1));

        var result = MarkdownImportValidator.Validate(oversized, "film.mp4");

        Assert.Equal(MarkdownImportFailure.UnsupportedExtension, result.FailureReason);
    }

    // -- Title derivation -----------------------------------------------------------------

    [Theory]
    [InlineData("wyklad.md", "wyklad")]
    [InlineData("Wykład 1 — wstęp.md", "Wykład 1 — wstęp")]
    [InlineData("notatki.z.wykladu.md", "notatki.z.wykladu")]
    [InlineData("  wyklad  .md", "wyklad")]
    public void The_title_comes_from_the_file_name(string fileName, string expected) =>
        Assert.Equal(expected, MarkdownImportValidator.DeriveTitle(fileName));

    [Theory]
    [InlineData("   .md")]
    [InlineData("")]
    [InlineData(null)]
    public void A_nameless_file_falls_back_to_a_placeholder(string? fileName) =>
        Assert.Equal(MarkdownImportValidator.FallbackTitle, MarkdownImportValidator.DeriveTitle(fileName));

    [Fact]
    public void An_over_long_title_is_truncated_to_the_column_width()
    {
        var derived = MarkdownImportValidator.DeriveTitle(new string('a', 300) + ".md");

        Assert.Equal(200, derived.Length);
    }

    [Fact]
    public void A_directory_part_is_stripped_from_the_title()
    {
        // Belt-and-braces: the browser sends a bare name, but a client that does not would
        // otherwise put a path into the title.
        Assert.Equal("wyklad", MarkdownImportValidator.DeriveTitle("/home/mati/notatki/wyklad.md"));
        Assert.Equal("wyklad", MarkdownImportValidator.DeriveTitle(@"C:\Users\mati\wyklad.md"));
    }

    /// <summary>
    /// Asserts the string survives strict UTF-8 encoding — the exact thing a lone surrogate
    /// breaks on its way to Postgres. Deliberately not "contains no surrogates": a whole emoji
    /// is a surrogate *pair*, and keeping one is correct.
    /// </summary>
    private static void AssertWellFormed(string value) =>
        Assert.Null(Record.Exception(() => StrictUtf8.GetBytes(value)));

    [Theory]
    // 199: the cut lands between the emoji's two halves, so it must be dropped whole.
    // 198: the cut lands just after it, so it must be kept whole. Both are off-by-one from
    // each other, which is where a guard like this gets it wrong.
    [InlineData(199)]
    [InlineData(198)]
    public void Truncating_a_title_leaves_a_well_formed_string(int padding)
    {
        var derived = MarkdownImportValidator.DeriveTitle(new string('a', padding) + "😀aaaaa.md");

        AssertWellFormed(derived);
        Assert.True(derived.Length <= 200);
    }

    [Fact]
    public void An_emoji_split_by_the_cut_is_dropped_whole_rather_than_halved()
    {
        var derived = MarkdownImportValidator.DeriveTitle(new string('a', 199) + "😀aaaaa.md");

        Assert.Equal(new string('a', 199), derived);
    }

    [Fact]
    public void An_emoji_that_fits_inside_the_cut_is_kept_whole()
    {
        var derived = MarkdownImportValidator.DeriveTitle(new string('a', 198) + "😀aaaaa.md");

        Assert.Equal(new string('a', 198) + "😀", derived);
        AssertWellFormed(derived);
    }

    [Theory]
    [InlineData(259)]
    [InlineData(258)]
    public void Truncating_a_file_name_leaves_a_well_formed_string(int padding)
    {
        var sanitized = MarkdownImportValidator.SanitizeFileName(new string('a', padding) + "😀aaaaa.md");

        AssertWellFormed(sanitized);
        Assert.True(sanitized.Length <= 260);
    }

    [Fact]
    public void A_title_ending_in_an_emoji_that_fits_keeps_it_whole()
    {
        // The guard must not eat a character that was never at risk.
        Assert.Equal("wyklad😀", MarkdownImportValidator.DeriveTitle("wyklad😀.md"));
    }

    [Fact]
    public void An_over_long_file_name_is_clamped_to_the_column_width()
    {
        // Without this the insert fails at SaveChanges with a database error instead of
        // anything the user could act on.
        var sanitized = MarkdownImportValidator.SanitizeFileName(new string('a', 400) + ".md");

        Assert.Equal(260, sanitized.Length);
    }
}
