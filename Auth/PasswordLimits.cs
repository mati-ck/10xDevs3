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
}
