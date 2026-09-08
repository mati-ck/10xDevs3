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
    /// The hint shown under a password field. States both bounds, and names the byte for the
    /// upper one because that is the unit that actually applies.
    /// </summary>
    public const string Hint =
        "Hasło musi mieć co najmniej 8 znaków i nie więcej niż 72 bajty "
        + "(polskie znaki liczą się podwójnie).";

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
            PasswordValidationFailure.TooShort =>
                $"Hasło musi mieć co najmniej {PasswordLimits.MinLength} znaków.",
            PasswordValidationFailure.TooManyBytes =>
                $"Hasło jest za długie — maksymalnie {PasswordLimits.MaxBytes} bajty. "
                + "Polskie znaki zajmują po dwa bajty, więc skróć hasło.",
            _ => "Podaj poprawne hasło."
        };

        return new ValidationResult(message, [validationContext.MemberName!]);
    }
}
