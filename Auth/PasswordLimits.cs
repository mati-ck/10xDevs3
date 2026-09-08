using System.Text;

namespace _10xnotes.Auth;

/// <summary>
/// How long a password may be — in the unit the layer beneath this one actually measures.
/// </summary>
/// <remarks>
/// This exists because the register form and the change-password form must not be able to
/// disagree about what a valid password is, and because the bound underneath them is not
/// measured in characters. GoTrue hashes with bcrypt, which reads at most 72 <em>bytes</em>
/// of the input, and rejects anything longer at <c>signup</c> and at <c>PUT /user</c> alike.
/// <para>
/// Bytes, not characters, and not UTF-16 code units. Three different units meet at this value:
/// <see cref="System.ComponentModel.DataAnnotations.StringLengthAttribute"/> counts characters,
/// <c>Text.TextLimits.Truncate</c> counts UTF-16 code units, and bcrypt counts UTF-8 bytes. A
/// Polish password is where they part company — every diacritic costs two bytes, so a 50-character
/// password can exceed 72 bytes. Measuring characters here would let the form accept a password
/// GoTrue then refuses with an English error the user cannot act on. Measure with
/// <see cref="ByteCount"/>; never with <c>.Length</c>.
/// </para>
/// <para>
/// This is the <c>lessons.md</c> rule "An advertised limit must be one every layer beneath it can
/// carry" applied to the password field: the form advertised 100 characters against GoTrue's 72
/// bytes, and a password in between failed as <see cref="AuthFailureReason.Unavailable"/>.
/// </para>
/// </remarks>
public static class PasswordLimits
{
    /// <summary>
    /// The most password bytes GoTrue will accept — bcrypt's own limit, which GoTrue enforces.
    /// </summary>
    /// <remarks>
    /// Pinned by <c>PasswordValidatorTests</c>. If Supabase ever changes this, this constant is
    /// where it surfaces; do not raise it on the strength of a form that seemed to work, because
    /// a longer password fails past the point where the form can report it.
    /// </remarks>
    public const int MaxBytes = 72;

    /// <summary>
    /// The shortest password the application accepts, in characters.
    /// </summary>
    /// <remarks>
    /// Characters is the right unit here, unlike <see cref="MaxBytes"/>: this is our own floor,
    /// not a bound imposed underneath us, and it is the number the register form has always
    /// advertised. Supabase's own minimum is configured per project and may be lower.
    /// </remarks>
    public const int MinLength = 8;

    /// <summary>
    /// What this password costs against <see cref="MaxBytes"/>.
    /// </summary>
    public static int ByteCount(string password) => Encoding.UTF8.GetByteCount(password);

    /// <summary>
    /// How many characters must come off the end for this password to fit, or 0 if it already
    /// does.
    /// </summary>
    /// <remarks>
    /// Exists so the user is never shown a byte count. "Maksymalnie 72 bajty" is unactionable —
    /// it asks someone to know how their own alphabet is encoded before they can guess how much
    /// to delete. This turns the same bound into the one instruction they can follow. Bytes stay
    /// the unit that is <em>enforced</em>, and stay out of every user-facing string.
    /// <para>
    /// Counts whole code points, not UTF-16 units, so an emoji costs one character rather than
    /// two and the answer never asks the user to delete half a surrogate pair.
    /// </para>
    /// </remarks>
    public static int ExcessCharacters(string password)
    {
        var bytes = 0;
        var seen = 0;
        var fitting = 0;

        foreach (var rune in password.EnumerateRunes())
        {
            seen++;
            bytes += rune.Utf8SequenceLength;

            if (bytes <= MaxBytes)
            {
                fitting = seen;
            }
        }

        return seen - fitting;
    }

    /// <summary>
    /// "znak" / "znaki" / "znaków" for <paramref name="count"/>.
    /// </summary>
    /// <remarks>
    /// Polish takes three forms, and the message built from <see cref="ExcessCharacters"/> is
    /// the only place in the project that has to interpolate a count into a noun. Getting it
    /// wrong reads as broken Polish, so the rule is here rather than approximated at the call
    /// site: 1 is singular; 2-4 take the plural, except in the teens; everything else takes the
    /// genitive plural.
    /// </remarks>
    public static string CharacterNoun(int count)
    {
        if (count == 1)
        {
            return "znak";
        }

        var lastTwo = count % 100;
        var last = count % 10;

        return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "znaki" : "znaków";
    }
}
