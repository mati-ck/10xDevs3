namespace _10xnotes.Auth;

/// <summary>Why a display name cannot be saved, in terms the UI can act on.</summary>
public enum DisplayNameValidationFailure
{
    None = 0,

    /// <summary>Longer than <see cref="DisplayNameValidator.MaxLength"/> characters once trimmed.</summary>
    TooLong
}

/// <summary>Outcome of validating a display name the user is about to save.</summary>
/// <remarks>
/// Carries the normalized name rather than only a flag, following <c>Notes.NoteValidationResult</c>:
/// without that the page could validate a trimmed name and then persist the untrimmed one, which
/// is how a length check silently stops matching the column width.
/// </remarks>
public sealed record DisplayNameValidationResult
{
    private DisplayNameValidationResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>
    /// The name to store: trimmed, or <c>null</c> when the user cleared it. <c>null</c> is a
    /// successful outcome, not a missing one.
    /// </summary>
    public string? DisplayName { get; private init; }

    public DisplayNameValidationFailure FailureReason { get; private init; }

    public static DisplayNameValidationResult Success(string? displayName) =>
        new() { Succeeded = true, DisplayName = displayName };

    public static DisplayNameValidationResult Failure(DisplayNameValidationFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };
}

/// <summary>
/// The rules deciding what a display name may be, and what "no display name" is stored as.
/// </summary>
/// <remarks>
/// This type exists mostly to settle the empty-versus-null question in one place. The column is
/// nullable and the trigger creates every profile with a null name, so <c>null</c> already means
/// "not set" — storing <c>""</c> for a blank submission would introduce a second spelling of the
/// same thing, and the nav would render it as an invisible entry rather than falling back to the
/// email. <see cref="Normalize"/> collapses both to <c>null</c>, and every write path goes
/// through it.
/// <para>
/// Pure by design: no EF, no components. Following <c>Notes.NoteValidator</c>, which is what makes
/// these branches testable in a project with no bUnit.
/// </para>
/// </remarks>
public static class DisplayNameValidator
{
    /// <summary>
    /// Matches the width configured for <c>Profile.DisplayName</c> in <c>Data.AppDbContext</c>.
    /// </summary>
    /// <remarks>
    /// Pinned against the EF model itself by <c>DisplayNameValidatorTests</c> rather than trusted
    /// to stay in step by hand — the two numbers live in different files and are set for different
    /// reasons, which is exactly how the password bound came to disagree with GoTrue's.
    /// <para>
    /// Characters, and the count is taken after trimming, because trimming is what decides the
    /// stored value. This is a UTF-16 code-unit count like every other length limit in the
    /// project; the column is <c>varchar(200)</c>, whose limit Postgres counts in characters, so
    /// an astral character costs two here and one there. The gap only matters for a name within
    /// two code units of the bound and made entirely of emoji, and it errs on the strict side.
    /// </para>
    /// </remarks>
    public const int MaxLength = 200;

    /// <summary>
    /// What this input is stored as: trimmed, or <c>null</c> when there is nothing left.
    /// </summary>
    /// <remarks>
    /// Trimmed unlike a password and like a note title — surrounding whitespace in a name is a
    /// typo, not content, and a name of nothing but spaces is the user clearing the field.
    /// </remarks>
    public static string? Normalize(string? input)
    {
        var trimmed = (input ?? string.Empty).Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Normalizes and then bounds the result. Clearing the name is a success carrying
    /// <c>null</c>, never a failure.
    /// </summary>
    public static DisplayNameValidationResult Validate(string? input)
    {
        var normalized = Normalize(input);

        if (normalized is { Length: > MaxLength })
        {
            return DisplayNameValidationResult.Failure(DisplayNameValidationFailure.TooLong);
        }

        return DisplayNameValidationResult.Success(normalized);
    }
}
