namespace _10xnotes.Auth;

/// <summary>Why an authentication attempt failed, in terms the UI can act on.</summary>
public enum AuthFailureReason
{
    None = 0,

    /// <summary>Email/password did not match, or the account does not exist.</summary>
    InvalidCredentials,

    /// <summary>An account already exists for this address.</summary>
    AlreadyRegistered,

    /// <summary>GoTrue rejected the password (too short or otherwise unacceptable).</summary>
    WeakPassword,

    /// <summary>The account exists but its email has not been confirmed.</summary>
    EmailNotConfirmed,

    /// <summary>Supabase was unreachable or returned something unexpected.</summary>
    Unavailable
}

/// <summary>
/// Outcome of a GoTrue call.
/// </summary>
/// <remarks>
/// Carries a classified <see cref="AuthFailureReason"/> rather than GoTrue's own message: that
/// text is English, and the PRD requires Polish copy. Callers map the reason to user-facing
/// wording, which is also where the "reveal nothing" policy is applied.
/// </remarks>
public sealed record AuthResult
{
    private AuthResult() { }

    public bool Succeeded { get; private init; }

    public Guid UserId { get; private init; }

    public string Email { get; private init; } = string.Empty;

    public AuthFailureReason FailureReason { get; private init; }

    public static AuthResult Success(Guid userId, string email) =>
        new() { Succeeded = true, UserId = userId, Email = email };

    public static AuthResult Failure(AuthFailureReason reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
