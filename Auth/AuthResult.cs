using System.Globalization;

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

    /// <summary>The new password is the one already in use.</summary>
    SamePassword,

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

    /// <summary>
    /// GoTrue's access token, when the call issued one. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// The password-grant endpoint returns one; <c>signup</c> may not, so an empty value is a
    /// valid success rather than a fault. This is the single exception to the rule that GoTrue's
    /// tokens are discarded (see <c>AuthCookie</c>): the change-password flow needs a bearer token
    /// for <c>PUT /user</c> and has no other way to obtain one.
    /// <para>
    /// It is <em>populated</em> on every password-grant response, not only the change-password
    /// one, so an ordinary sign-in and the delete flow's proof also carry a live token in this
    /// property. Nothing reads it there. What is guaranteed is narrower than "only that flow
    /// sees it": the token is never written to the cookie, the database, or a field, never
    /// logged (<see cref="PrintMembers"/> redacts it), and never returned by
    /// <c>SupabaseAuthClient.ChangePasswordAsync</c>, which uses it and drops it. Do not widen
    /// that set.
    /// </para>
    /// </remarks>
    public string AccessToken { get; private init; } = string.Empty;

    public AuthFailureReason FailureReason { get; private init; }

    public static AuthResult Success(Guid userId, string email, string accessToken = "") =>
        new() { Succeeded = true, UserId = userId, Email = email, AccessToken = accessToken };

    public static AuthResult Failure(AuthFailureReason reason) =>
        new() { Succeeded = false, FailureReason = reason };

    /// <summary>
    /// Keeps the access token out of <see cref="object.ToString"/>, which a log statement or a
    /// debugger reaches by accident. A record prints every property by default, and a bearer
    /// token in a log line is a credential in a log line.
    /// </summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Succeeded)} = {Succeeded}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(UserId)} = {UserId}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Email)} = {Email}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(AccessToken)} = {(AccessToken.Length == 0 ? "<none>" : "<redacted>")}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{nameof(FailureReason)} = {FailureReason}");
        return true;
    }
}
