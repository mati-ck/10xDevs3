using _10xnotes.Auth;
using _10xnotes.Data;
using _10xnotes.Data.Entities;
using Microsoft.Data.Sqlite;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the rules that decide what reaches <c>profiles.display_name</c>, and — the point of the
/// type — that "no name" has exactly one spelling.
/// </summary>
/// <remarks>
/// The column is nullable and the <c>on_auth_user_created</c> trigger creates every profile with a
/// null name, so <c>null</c> already means "not set". Storing <c>""</c> for a blank submission
/// would add a second spelling of the same thing, and only one of them makes the nav fall back to
/// the email — the other renders an invisible entry.
/// </remarks>
public sealed class DisplayNameValidatorTests
{
    // -- Normalization ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Anything_blank_normalizes_to_null(string? input)
    {
        Assert.Null(DisplayNameValidator.Normalize(input));

        var result = DisplayNameValidator.Validate(input);

        // Clearing the name is a success carrying null, never a failure.
        Assert.True(result.Succeeded);
        Assert.Null(result.DisplayName);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_away()
    {
        var result = DisplayNameValidator.Validate("  Ala Kowalska  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Ala Kowalska", result.DisplayName);
    }

    /// <summary>
    /// Only the ends. Whitespace inside a name is part of it, so trimming must not collapse it.
    /// </summary>
    [Fact]
    public void Whitespace_inside_a_name_is_left_alone()
    {
        Assert.Equal("Ala  Kowalska", DisplayNameValidator.Normalize(" Ala  Kowalska "));
    }

    // -- The bound -------------------------------------------------------------------------

    [Fact]
    public void A_name_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new string('a', DisplayNameValidator.MaxLength);

        var result = DisplayNameValidator.Validate(atLimit);

        Assert.True(result.Succeeded);
        Assert.Equal(atLimit, result.DisplayName);
    }

    [Fact]
    public void A_name_one_character_over_the_limit_is_rejected()
    {
        var result = DisplayNameValidator.Validate(new string('a', DisplayNameValidator.MaxLength + 1));

        Assert.False(result.Succeeded);
        Assert.Equal(DisplayNameValidationFailure.TooLong, result.FailureReason);
    }

    /// <summary>
    /// The bound is measured after trimming, because trimming is what decides the stored value.
    /// A name that only exceeds the limit because of padding must be accepted, not refused.
    /// </summary>
    [Fact]
    public void The_limit_is_measured_after_trimming()
    {
        var padded = "  " + new string('a', DisplayNameValidator.MaxLength) + "  ";

        Assert.True(padded.Length > DisplayNameValidator.MaxLength);
        Assert.True(DisplayNameValidator.Validate(padded).Succeeded);
    }

    /// <summary>
    /// The advertised limit against the column that has to hold it — the <c>lessons.md</c> rule,
    /// read off the EF model rather than restated, so the two cannot drift apart in different
    /// files the way the password bound did against GoTrue's.
    /// </summary>
    [Fact]
    public void The_limit_matches_the_column_width()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        using var context = SqliteTestContext.Create(connection, Guid.Empty);

        var configured = context.Model
            .FindEntityType(typeof(Profile))!
            .FindProperty(nameof(Profile.DisplayName))!
            .GetMaxLength();

        Assert.Equal(DisplayNameValidator.MaxLength, configured);
    }

    // -- Characters that are not letters -----------------------------------------------------

    /// <summary>
    /// An emoji is a surrogate pair, so it costs two of the 200 here and one character in
    /// Postgres. The gap only matters within two units of the bound and it errs strict, but it
    /// must not throw or mangle the name.
    /// </summary>
    [Fact]
    public void A_name_containing_an_emoji_survives_intact()
    {
        var result = DisplayNameValidator.Validate("Ala 🎓 Kowalska");

        Assert.True(result.Succeeded);
        Assert.Equal("Ala 🎓 Kowalska", result.DisplayName);
    }

    [Fact]
    public void A_name_of_emoji_is_bounded_by_utf16_units_not_by_glyphs()
    {
        var hundredAndOneEmoji = string.Concat(
            Enumerable.Repeat("🎓", (DisplayNameValidator.MaxLength / 2) + 1));

        Assert.False(DisplayNameValidator.Validate(hundredAndOneEmoji).Succeeded);
    }

    [Fact]
    public void Polish_diacritics_are_preserved()
    {
        Assert.Equal("Zażółć gęślą jaźń", DisplayNameValidator.Normalize(" Zażółć gęślą jaźń "));
    }
}
