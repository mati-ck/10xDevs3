namespace _10xnotes.Generation;

/// <summary>
/// Every way generating a note can fail, named so the UI maps a value onto Polish copy instead
/// of guessing from an exception type.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Auth.AuthFailureReason"/>: no provider prose ever reaches the user, so the
/// classification has to carry everything the UI needs to say something useful.
/// </remarks>
public enum GenerationFailure
{
    /// <summary>The provider could not be reached, refused our credentials, or answered with
    /// something unclassifiable. Deliberately neutral — it never blames the user.</summary>
    Unavailable,

    /// <summary>The provider rate-limited us. Passes in minutes, unlike <see cref="QuotaExceeded"/>.</summary>
    RateLimited,

    /// <summary>Our own daily per-user ledger is exhausted. Passes at midnight.</summary>
    QuotaExceeded,

    /// <summary>The material did not fit the model's context window.</summary>
    TooLong,

    /// <summary>The generation exceeded <see cref="AiOptions.RequestTimeout"/>.</summary>
    TimedOut,

    /// <summary>The caller walked away — navigated off the page, or closed the circuit.</summary>
    Cancelled,

    /// <summary>The provider answered successfully but produced no text.</summary>
    Empty
}
