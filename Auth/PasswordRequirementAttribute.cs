using System.ComponentModel.DataAnnotations;

namespace _10xnotes.Auth;

/// <summary>
/// Applies <see cref="PasswordValidator"/> to a form field, so a form states the same bounds the
/// rest of the application enforces.
/// </summary>
/// <remarks>
/// A <see cref="ValidationAttribute"/> rather than a check inside each page, because the failure
/// this change exists to fix was two forms disagreeing: the register form carried
/// <c>StringLength(100)</c> against GoTrue's 72 bytes, and nothing connected the two numbers. The
/// only way an attribute can drift from the rule now is by being removed, which is visible.
/// <para>
/// Carries the Polish wording as well as the rule. The wording is shared for the same reason the
/// rule is — a user who is told "at least 8 characters" on one form and something else on another
/// has been told the bounds are different when they are not.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PasswordRequirementAttribute : ValidationAttribute
{
    /// <summary>
    /// The hint shown under a password field.
    /// </summary>
    /// <remarks>
    /// States the minimum only. The maximum is deliberately absent: it is a byte bound, so there
    /// is no character number that is both true and useful — 72 for a Latin password, 36 for a
    /// Polish one, 18 for emoji — and quoting bytes tells the user about bcrypt instead of about
    /// their password. The rare over-long case is handled by an error that says exactly how many
    /// characters to remove, which is the only form of the limit anybody can act on.
    /// </remarks>
    public static readonly string Hint =
        $"Hasło musi mieć co najmniej {PasswordLimits.MinLength} znaków.";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var result = PasswordValidator.Validate(value as string);

        if (result.Succeeded)
        {
            return ValidationResult.Success;
        }

        var message = result.FailureReason switch
        {
            PasswordValidationFailure.Missing => "Podaj hasło.",
            // Deliberately not the same sentence as Hint, which sits directly above it on both
            // forms: repeating the rule verbatim under itself reads as a rendering fault rather
            // than as feedback. This reacts to what was typed, symmetric with the too-long case.
            PasswordValidationFailure.TooShort =>
                $"Hasło jest za krótkie — użyj co najmniej {PasswordLimits.MinLength} znaków.",
            PasswordValidationFailure.TooManyBytes => TooLongMessage(value as string ?? string.Empty),
            _ => "Podaj poprawne hasło."
        };

        return new ValidationResult(message, [validationContext.MemberName!]);
    }

    /// <summary>
    /// Says how much to delete rather than what the limit is, and never mentions a byte.
    /// </summary>
    private static string TooLongMessage(string password)
    {
        var excess = PasswordLimits.ExcessCharacters(password);

        return $"Hasło jest za długie — skróć je o co najmniej {excess} "
            + $"{PasswordLimits.CharacterNoun(excess)}.";
    }
}
