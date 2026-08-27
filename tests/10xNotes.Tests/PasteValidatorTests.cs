using _10xnotes.SourceMaterials;

namespace _10xNotes.Tests;

/// <summary>
/// Pins every branch of the paste rules. These decide what reaches the database and what reaches
/// the AI prompt — the same job <see cref="MarkdownImportValidatorTests"/> does for the file path,
/// and the only way either set of branches gets tested at all in a project with no bUnit.
/// </summary>
public sealed class PasteValidatorTests
{
    // -- Validate: emptiness --------------------------------------------------------------

    [Fact]
    public void A_normal_paste_is_accepted()
    {
        var result = PasteValidator.Validate("# Wykład\n\nTreść notatek z wykładu.");

        Assert.True(result.Succeeded);
        Assert.Equal(PasteFailure.None, result.FailureReason);
        Assert.Equal("# Wykład\n\nTreść notatek z wykładu.", result.Content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData(" \t\r\n ")]
    public void A_paste_with_no_actual_text_is_rejected(string? content)
    {
        var result = PasteValidator.Validate(content);

        Assert.False(result.Succeeded);
        Assert.Equal(PasteFailure.Empty, result.FailureReason);
    }

    // -- Validate: length bound -----------------------------------------------------------

    [Fact]
    public void A_paste_exactly_at_the_limit_is_accepted()
    {
        var result = PasteValidator.Validate(new string('a', PasteValidator.MaxContentLength));

        Assert.True(result.Succeeded);
        Assert.Equal(PasteValidator.MaxContentLength, result.Content.Length);
    }

    [Fact]
    public void A_paste_one_character_over_the_limit_is_rejected()
    {
        var result = PasteValidator.Validate(new string('a', PasteValidator.MaxContentLength + 1));

        Assert.False(result.Succeeded);
        Assert.Equal(PasteFailure.TooLong, result.FailureReason);
    }

    /// <summary>
    /// The limit counts UTF-16 code units, not bytes — the same trap the comment on
    /// <see cref="PasteValidator.MaxContentLength"/> warns about. Polish text at the limit weighs
    /// roughly twice as many bytes and is still accepted, on purpose.
    /// </summary>
    [Fact]
    public void The_limit_counts_characters_rather_than_bytes()
    {
        var polish = new string('ż', PasteValidator.MaxContentLength);

        Assert.True(PasteValidator.Validate(polish).Succeeded);
        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(polish) > MarkdownImportValidator.MaxFileSizeBytes,
            "If this ever stops holding, the two limits have quietly become the same size and the "
            + "deliberate asymmetry documented on PasteValidator.MaxContentLength is gone.");
    }

    // -- Validate: content is verbatim ----------------------------------------------------

    /// <summary>
    /// Leading whitespace is what makes an indented code block a code block, so the text is stored
    /// exactly as pasted — the same posture <c>NoteValidator.Validate</c> takes on a note body.
    /// </summary>
    [Fact]
    public void The_content_is_not_trimmed()
    {
        const string content = "    kod w bloku\n\nreszta\n\n";

        var result = PasteValidator.Validate(content);

        Assert.True(result.Succeeded);
        Assert.Equal(content, result.Content);
    }

    /// <summary>
    /// Unlike the import path, a BOM is not stripped: a BOM is an artefact of how a file is
    /// encoded, and the clipboard does not carry one. See "What We're NOT Doing" in the plan.
    /// </summary>
    [Fact]
    public void A_leading_bom_is_left_alone()
    {
        var result = PasteValidator.Validate("﻿Treść");

        Assert.True(result.Succeeded);
        Assert.Equal("﻿Treść", result.Content);
    }

    // -- DeriveTitle ----------------------------------------------------------------------

    [Fact]
    public void The_title_comes_from_the_first_line()
    {
        Assert.Equal("Wykład z historii", PasteValidator.DeriveTitle("Wykład z historii\nreszta treści"));
    }

    [Theory]
    [InlineData("# Wykład", "Wykład")]
    [InlineData("## Wykład", "Wykład")]
    [InlineData("###### Wykład", "Wykład")]
    [InlineData("#Wykład", "Wykład")]
    [InlineData("   ## Wykład   ", "Wykład")]
    public void A_markdown_heading_marker_is_stripped(string content, string expected) =>
        Assert.Equal(expected, PasteValidator.DeriveTitle(content));

    /// <summary>Only the leading run — a hash inside the line is part of the text.</summary>
    [Fact]
    public void A_hash_inside_the_line_is_kept()
    {
        Assert.Equal("Wykład #3", PasteValidator.DeriveTitle("# Wykład #3\ndalej"));
    }

    [Fact]
    public void Leading_blank_lines_are_skipped()
    {
        Assert.Equal("Wykład", PasteValidator.DeriveTitle("\n\n   \n# Wykład\ntreść"));
    }

    [Fact]
    public void A_crlf_paste_does_not_keep_the_carriage_return()
    {
        Assert.Equal("Wykład", PasteValidator.DeriveTitle("# Wykład\r\ntreść"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t\n  ")]
    [InlineData("#\n##\n")]
    public void Nothing_usable_falls_back(string? content) =>
        Assert.Equal(PasteValidator.FallbackTitle, PasteValidator.DeriveTitle(content));

    [Fact]
    public void The_fallback_matches_the_import_paths_fallback() =>
        Assert.Equal(MarkdownImportValidator.FallbackTitle, PasteValidator.FallbackTitle);

    // -- DeriveTitle: column width --------------------------------------------------------

    [Fact]
    public void A_long_first_line_is_cut_to_the_column_width()
    {
        var title = PasteValidator.DeriveTitle(new string('a', 500) + "\nreszta");

        Assert.Equal(200, title.Length);
    }

    /// <summary>
    /// The surrogate-pair case <c>TextLimits.Truncate</c> exists for: cutting at 200 must not
    /// leave half of an emoji behind, because a lone surrogate is not a valid string on the way to
    /// Postgres. 199 'a's puts the pair astride the cut.
    /// </summary>
    [Fact]
    public void A_cut_landing_inside_a_surrogate_pair_drops_the_whole_character()
    {
        var title = PasteValidator.DeriveTitle(new string('a', 199) + "\U0001F600ogon");

        Assert.Equal(199, title.Length);
        Assert.DoesNotContain(title, c => char.IsSurrogate(c));
    }
}
