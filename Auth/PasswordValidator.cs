namespace _10xnotes.Auth;

/// <summary>Why a password cannot be used, in terms the UI can act on.</summary>
public enum PasswordValidationFailure
{
    None = 0,

    /// <summary>Nothing was supplied.</summary>
    Missing,

    /// <summary>Shorter than <see cref="PasswordLimits.MinLength"/> characters.</summary>
    TooShort,

    /// <summary>Longer than <see cref="PasswordLimits.MaxBytes"/> UTF-8 bytes.</summary>
    TooManyBytes
}

/// <summary>Outcome of checking a password the user is about to set.</summary>
/// <remarks>
/// Mirrors <c>Notes.NoteValidationResult</c>: a classified reason rather than a message, because
/// the reason is a fact about the password while the wording is a UI concern and the PRD requires
/// Polish copy.
/// </remarks>
public sealed record PasswordValidationResult
{
    private PasswordValidationResult() { }

    public bool Succeeded { get; private init; }

    public PasswordValidationFailure FailureReason { get; private init; }

    public static PasswordValidationResult Success() => new() { Succeeded = true };

    public static PasswordValidationResult Failure(PasswordValidationFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}

/// <summary>
/// The single rule deciding whether a password may be set, shared by every form that sets one.
/// </summary>
/// <remarks>
/// Pure by design: no forms, no HTTP, no EF — which is what lets both bounds be tested without a
/// browser and a Supabase project, following <c>Notes.NoteValidator</c>. The password is never
/// trimmed: leading and trailing spaces are part of a password, unlike a title.
/// </remarks>
public static class PasswordValidator
{
    /// <summary>
    /// Runs the rules in order: missing before too-short before too-long, so the user is told the
    /// most specific thing that is wrong.
    /// </summary>
    public static PasswordValidationResult Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PasswordValidationResult.Failure(PasswordValidationFailure.Missing);
        }

        if (password.Length < PasswordLimits.MinLength)
        {
            return PasswordValidationResult.Failure(PasswordValidationFailure.TooShort);
        }

        // Bytes, deliberately — see the remarks on PasswordLimits. A character count passes
        // Polish passwords that bcrypt then refuses.
        if (PasswordLimits.ByteCount(password) > PasswordLimits.MaxBytes)
        {
            return PasswordValidationResult.Failure(PasswordValidationFailure.TooManyBytes);
        }

        return PasswordValidationResult.Success();
    }
}
