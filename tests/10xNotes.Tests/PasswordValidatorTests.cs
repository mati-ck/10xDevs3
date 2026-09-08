using System.ComponentModel.DataAnnotations;
using System.Text;
using _10xnotes.Auth;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the one password bound the application advertises to the one GoTrue actually enforces.
/// </summary>
/// <remarks>
/// This is the <c>lessons.md</c> rule "An advertised limit must be one every layer beneath it can
/// carry" applied a second time. The first instance was a note editor advertising 64 KB over a
/// SignalR default of 32 KB; this one was the register form advertising 100 characters over
/// bcrypt's 72 bytes, so a password in between reached GoTrue and came back as a generic failure.
/// <para>
/// The load-bearing case is <see cref="A_polish_password_under_the_character_count_can_still_exceed_the_byte_bound"/>:
/// characters and bytes are different units and Polish is where they diverge, which is precisely
/// the mistake a "just use StringLength" fix would reintroduce.
/// </para>
/// </remarks>
public sealed class PasswordValidatorTests
{
    /// <summary>
    /// bcrypt reads at most 72 bytes of its input and GoTrue rejects anything longer, at
    /// <c>signup</c> and at <c>PUT /user</c> alike. Changing this constant changes what both forms
    /// accept, so it is pinned rather than left to drift alongside a form that seemed to work.
    /// </summary>
    [Fact]
    public void The_byte_bound_is_gotrues_bcrypt_limit()
    {
        Assert.Equal(72, PasswordLimits.MaxBytes);
    }

    /// <summary>
    /// The minimum is the number the register form has always shown, kept in characters because
    /// it is our own floor rather than a bound imposed underneath us.
    /// </summary>
    [Fact]
    public void The_minimum_length_is_the_one_the_register_form_advertises()
    {
        Assert.Equal(8, PasswordLimits.MinLength);
    }

    // -- Missing ---------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_password_is_rejected(string? password)
    {
        var result = PasswordValidator.Validate(password);

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordValidationFailure.Missing, result.FailureReason);
    }

    // -- Too short -------------------------------------------------------------------------

    [Fact]
    public void A_password_one_character_below_the_minimum_is_rejected()
    {
        var result = PasswordValidator.Validate(new string('a', PasswordLimits.MinLength - 1));

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordValidationFailure.TooShort, result.FailureReason);
    }

    [Fact]
    public void A_password_exactly_at_the_minimum_is_accepted()
    {
        Assert.True(PasswordValidator.Validate(new string('a', PasswordLimits.MinLength)).Succeeded);
    }

    /// <summary>
    /// Whitespace is part of a password, unlike a title — so a passphrase of spaces is short, not
    /// blank, and the validator must not trim it into emptiness.
    /// </summary>
    [Fact]
    public void Whitespace_counts_towards_the_length_rather_than_being_trimmed_away()
    {
        var result = PasswordValidator.Validate("   haslo   ");

        Assert.True(result.Succeeded);
    }

    // -- Too many bytes --------------------------------------------------------------------

    [Fact]
    public void A_password_exactly_at_the_byte_bound_is_accepted()
    {
        var atBound = new string('a', PasswordLimits.MaxBytes);

        Assert.Equal(PasswordLimits.MaxBytes, Encoding.UTF8.GetByteCount(atBound));
        Assert.True(PasswordValidator.Validate(atBound).Succeeded);
    }

    [Fact]
    public void A_password_one_byte_over_the_bound_is_rejected()
    {
        var result = PasswordValidator.Validate(new string('a', PasswordLimits.MaxBytes + 1));

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordValidationFailure.TooManyBytes, result.FailureReason);
    }

    /// <summary>
    /// The case the shared constant exists to prevent: 40 Polish characters are 80 UTF-8 bytes, so
    /// a character-based check would pass this through to a GoTrue rejection the user cannot act
    /// on. If this test ever fails, the rule has been rewritten to count the wrong unit.
    /// </summary>
    [Fact]
    public void A_polish_password_under_the_character_count_can_still_exceed_the_byte_bound()
    {
        var polish = new string('ż', 40);

        Assert.True(polish.Length < PasswordLimits.MaxBytes);
        Assert.True(Encoding.UTF8.GetByteCount(polish) > PasswordLimits.MaxBytes);

        var result = PasswordValidator.Validate(polish);

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordValidationFailure.TooManyBytes, result.FailureReason);
    }

    /// <summary>
    /// A four-byte character costs four of the 72, not one and not two — the surrogate pair must
    /// not be counted as two UTF-16 units either.
    /// </summary>
    [Fact]
    public void An_astral_character_costs_its_utf8_bytes()
    {
        var emoji = string.Concat(Enumerable.Repeat("🔒", 18));

        Assert.Equal(72, Encoding.UTF8.GetByteCount(emoji));
        Assert.True(PasswordValidator.Validate(emoji).Succeeded);
        Assert.False(PasswordValidator.Validate(emoji + "🔒").Succeeded);
    }

    // -- Valid -----------------------------------------------------------------------------

    [Fact]
    public void An_ordinary_password_is_accepted()
    {
        var result = PasswordValidator.Validate("haslo12345");

        Assert.True(result.Succeeded);
        Assert.Equal(PasswordValidationFailure.None, result.FailureReason);
    }

    // -- The form attribute ------------------------------------------------------------------

    /// <summary>
    /// The forms reach the rule through a <see cref="ValidationAttribute"/>, so the attribute is
    /// what has to agree with the validator — an attribute that silently passed everything would
    /// leave both forms unbounded while every test above still passed.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("krotkie", false)]
    [InlineData("haslo12345", true)]
    public void The_form_attribute_applies_the_same_rule(string? password, bool expectedValid)
    {
        Assert.Equal(expectedValid, ValidateThroughAttribute(password) is null);
    }

    /// <summary>
    /// The user is never shown a byte count. "Maksymalnie 72 bajty" is unactionable — it asks
    /// someone to know how their alphabet is encoded before they can guess how much to delete —
    /// so the message says how many characters to remove instead. Bytes stay the enforced unit
    /// and stay out of every string the user reads.
    /// </summary>
    [Theory]
    [InlineData(40, 4)]   // 40 Polish characters are 80 bytes; 36 fit, so 4 must go.
    [InlineData(37, 1)]   // 74 bytes: one character over.
    public void An_over_long_polish_password_is_told_how_much_to_remove(int length, int excess)
    {
        var message = ValidateThroughAttribute(new string('ż', length));

        Assert.NotNull(message);
        Assert.Contains($"o co najmniej {excess} ", message);
        Assert.DoesNotContain("bajt", message);
        Assert.DoesNotContain(PasswordLimits.MaxBytes.ToString(), message);
    }

    [Theory]
    [InlineData(73, 1)]
    [InlineData(74, 2)]
    [InlineData(77, 5)]
    public void An_over_long_latin_password_counts_characters_one_for_one(int length, int excess)
    {
        Assert.Equal(excess, PasswordLimits.ExcessCharacters(new string('a', length)));
    }

    [Fact]
    public void A_password_that_fits_has_nothing_to_remove()
    {
        Assert.Equal(0, PasswordLimits.ExcessCharacters(new string('a', PasswordLimits.MaxBytes)));
        Assert.Equal(0, PasswordLimits.ExcessCharacters("haslo12345"));
    }

    /// <summary>
    /// An emoji is one character to the user and four bytes to bcrypt. The count must be the
    /// user's, or the message asks them to delete half a surrogate pair.
    /// </summary>
    [Fact]
    public void An_emoji_counts_as_one_character_to_remove_not_two()
    {
        // 19 emoji are 76 bytes; 18 fit, so exactly one emoji must go.
        var emoji = string.Concat(Enumerable.Repeat("🔒", 19));

        Assert.Equal(1, PasswordLimits.ExcessCharacters(emoji));
        Assert.Contains("o co najmniej 1 znak.", ValidateThroughAttribute(emoji));
    }

    /// <summary>
    /// Polish takes three noun forms and the message interpolates a count, so getting this wrong
    /// reads as broken Polish in a Polish-language product.
    /// </summary>
    [Theory]
    [InlineData(1, "znak")]
    [InlineData(2, "znaki")]
    [InlineData(4, "znaki")]
    [InlineData(5, "znaków")]
    [InlineData(12, "znaków")]   // the teens take the genitive despite ending in 2
    [InlineData(14, "znaków")]
    [InlineData(22, "znaki")]
    [InlineData(25, "znaków")]
    [InlineData(112, "znaków")]
    [InlineData(122, "znaki")]
    public void The_character_count_takes_the_right_polish_noun_form(int count, string expected)
    {
        Assert.Equal(expected, PasswordLimits.CharacterNoun(count));
    }

    private static string? ValidateThroughAttribute(string? password)
    {
        var model = new PasswordHolder { Password = password! };
        var results = new List<ValidationResult>();

        Validator.TryValidateProperty(
            password,
            new ValidationContext(model) { MemberName = nameof(PasswordHolder.Password) },
            results);

        return results.SingleOrDefault()?.ErrorMessage;
    }

    private sealed class PasswordHolder
    {
        [PasswordRequirement]
        public string Password { get; set; } = string.Empty;
    }
}
