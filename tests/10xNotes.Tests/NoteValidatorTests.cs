using _10xnotes.Notes;

namespace _10xNotes.Tests;

/// <summary>
/// Pins every branch of the save rules. These decide what reaches the notes table and, because
/// saving is what "accepted" means in this product, what the 75% acceptance measurement counts —
/// so a wrong branch here is not a UI annoyance but a number nobody can trust.
/// </summary>
public sealed class NoteValidatorTests
{
    private const string ValidTitle = "Notatka z wykładu";
    private const string ValidContent = "## Streszczenie\n\n- punkt pierwszy";

    // -- Title ----------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void A_blank_title_is_rejected(string? title)
    {
        var result = NoteValidator.Validate(title, ValidContent);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteValidationFailure.TitleEmpty, result.FailureReason);
    }

    [Fact]
    public void A_title_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new string('a', NoteValidator.MaxTitleLength);

        var result = NoteValidator.Validate(atLimit, ValidContent);

        Assert.True(result.Succeeded);
        Assert.Equal(atLimit, result.Title);
    }

    [Fact]
    public void A_title_one_character_over_the_limit_is_rejected()
    {
        var overLimit = new string('a', NoteValidator.MaxTitleLength + 1);

        var result = NoteValidator.Validate(overLimit, ValidContent);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteValidationFailure.TitleTooLong, result.FailureReason);
    }

    [Fact]
    public void The_accepted_title_is_trimmed()
    {
        // The trimmed value is what gets saved, so the length check and the column width judge
        // the same string. Returning the raw title on success would let a padded title pass a
        // 200-character check and then fail at SaveChanges.
        var result = NoteValidator.Validate("  Notatka  ", ValidContent);

        Assert.True(result.Succeeded);
        Assert.Equal("Notatka", result.Title);
    }

    [Fact]
    public void A_title_that_is_only_whitespace_around_the_limit_is_judged_after_trimming()
    {
        var padded = "   " + new string('a', NoteValidator.MaxTitleLength) + "   ";

        var result = NoteValidator.Validate(padded, ValidContent);

        Assert.True(result.Succeeded);
    }

    // -- Content --------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t  \n")]
    [InlineData(null)]
    public void A_blank_note_body_is_rejected(string? content)
    {
        var result = NoteValidator.Validate(ValidTitle, content);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteValidationFailure.ContentEmpty, result.FailureReason);
    }

    [Fact]
    public void A_body_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new string('a', NoteValidator.MaxContentLength);

        var result = NoteValidator.Validate(ValidTitle, atLimit);

        Assert.True(result.Succeeded);
        Assert.Equal(atLimit.Length, result.Content.Length);
    }

    [Fact]
    public void A_body_one_character_over_the_limit_is_rejected()
    {
        var overLimit = new string('a', NoteValidator.MaxContentLength + 1);

        var result = NoteValidator.Validate(ValidTitle, overLimit);

        Assert.False(result.Succeeded);
        Assert.Equal(NoteValidationFailure.ContentTooLong, result.FailureReason);
    }

    [Fact]
    public void The_accepted_body_keeps_its_leading_whitespace()
    {
        // Indentation is what makes an indented code block a code block, so trimming the body
        // would silently rewrite the user's Markdown into something that renders differently.
        var indented = "    var x = 1;\n";

        var result = NoteValidator.Validate(ValidTitle, indented);

        Assert.True(result.Succeeded);
        Assert.Equal(indented, result.Content);
    }

    [Fact]
    public void An_empty_title_is_reported_before_an_empty_body()
    {
        // Order matters: the user should be told the most specific thing that is wrong, and the
        // title is the field they can fix without rewriting anything.
        var result = NoteValidator.Validate("", "");

        Assert.Equal(NoteValidationFailure.TitleEmpty, result.FailureReason);
    }

    // -- Derived title --------------------------------------------------------------------

    [Fact]
    public void The_starting_title_comes_from_the_material()
    {
        Assert.Equal("Wykład o sieciach", NoteValidator.DeriveTitle("Wykład o sieciach"));
    }

    [Fact]
    public void The_starting_title_is_trimmed()
    {
        Assert.Equal("Wykład", NoteValidator.DeriveTitle("  Wykład  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_material_with_no_usable_title_falls_back(string? materialTitle)
    {
        // The title is required, so the editor must never open with an empty one — that would
        // turn "save" into an error the user did not cause.
        Assert.Equal(NoteValidator.FallbackTitle, NoteValidator.DeriveTitle(materialTitle));
    }

    [Fact]
    public void A_material_title_over_the_limit_is_truncated_to_it()
    {
        var overLimit = new string('a', NoteValidator.MaxTitleLength + 50);

        var derived = NoteValidator.DeriveTitle(overLimit);

        Assert.Equal(NoteValidator.MaxTitleLength, derived.Length);
        Assert.True(NoteValidator.Validate(derived, ValidContent).Succeeded);
    }

    [Fact]
    public void Truncation_does_not_split_a_surrogate_pair()
    {
        // A cut landing inside a surrogate pair leaves a lone surrogate, which is not a valid
        // string: it either encodes to U+FFFD or throws on the way to Postgres. Emoji in a
        // material title are routine; Polish diacritics are all BMP, so the naive version passes
        // every realistic test and fails here.
        var withEmoji = new string('a', NoteValidator.MaxTitleLength - 1) + "\U0001F600";

        var derived = NoteValidator.DeriveTitle(withEmoji);

        Assert.Equal(NoteValidator.MaxTitleLength - 1, derived.Length);
        Assert.DoesNotContain(derived, char.IsSurrogate);
    }
}
