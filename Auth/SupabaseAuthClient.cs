using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace _10xnotes.Auth;

/// <summary>
/// Talks to Supabase Auth (GoTrue) over its REST API.
/// </summary>
/// <remarks>
/// Supabase is used purely as a credential store: we create and verify credentials here, then
/// mint our own cookie and discard GoTrue's tokens. That is why so few endpoints are needed and
/// why no SDK is taken on.
/// </remarks>
public sealed class SupabaseAuthClient(HttpClient httpClient, ILogger<SupabaseAuthClient> logger)
{
    public Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
        => PostCredentialsAsync("signup", email, password, cancellationToken);

    public Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
        => PostCredentialsAsync(TokenPath, email, password, cancellationToken);

    private const string TokenPath = "token?grant_type=password";

    /// <summary>
    /// Changes the signed-in user's password, having first proved they know the current one.
    /// </summary>
    /// <remarks>
    /// Two round trips, deliberately fused into one method so no caller can perform the second
    /// without the first. Re-authenticating against <c>token?grant_type=password</c> is what
    /// proves the current password and what yields the bearer token <c>PUT /user</c> demands —
    /// the application holds no GoTrue session of its own to reuse, by design.
    /// <para>
    /// Because the two steps are one method, "the old password was checked" is a structural
    /// property of this flow rather than a check somewhere that could be dropped. A failed
    /// re-authentication returns immediately, so the <c>PUT</c> is never issued and a wrong
    /// current password surfaces as <see cref="AuthFailureReason.InvalidCredentials"/>.
    /// </para>
    /// <para>
    /// The access token is a local. It is never returned to the caller, written to the cookie,
    /// stored, or logged — which is what keeps "the cookie is the only session concept" true.
    /// The refresh token is dropped as everywhere else.
    /// </para>
    /// </remarks>
    public async Task<AuthResult> ChangePasswordAsync(
        string email,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var reauthentication = await PostCredentialsAsync(TokenPath, email, currentPassword, cancellationToken);

        if (!reauthentication.Succeeded)
        {
            // Returned unchanged: a wrong current password must read as InvalidCredentials, not
            // as a failure of the change itself.
            return reauthentication;
        }

        if (reauthentication.AccessToken.Length == 0)
        {
            // Re-authentication succeeded but issued no token, so the PUT cannot be authorized.
            // Fail closed rather than sending an unauthenticated request that GoTrue would
            // reject as if the password were bad.
            logger.LogError("Supabase Auth re-authentication succeeded but returned no access token.");
            return AuthResult.Failure(AuthFailureReason.Unavailable);
        }

        return await PutPasswordAsync(reauthentication.AccessToken, newPassword, cancellationToken);
    }

    /// <summary>
    /// The second leg: <c>PUT /user</c> with the bearer token, carrying only the new password.
    /// </summary>
    /// <remarks>
    /// Only <c>password</c> travels in the body. <c>PUT /user</c> also accepts <c>email</c> and
    /// <c>data</c>, and sending either would change something nobody asked to change — an email
    /// in particular starts a confirmation flow this application cannot complete.
    /// </remarks>
    private async Task<AuthResult> PutPasswordAsync(
        string accessToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "user")
        {
            Content = JsonContent.Create(new { password = newPassword })
        };

        // The apikey header travels on every request from the shared HttpClient; this one also
        // needs the user's bearer token, which is what GoTrue's requireAuthentication reads.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        string body;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Supabase Auth request to user failed.");
            return AuthResult.Failure(AuthFailureReason.Unavailable);
        }

        if (!response.IsSuccessStatusCode)
        {
            var reason = ClassifyFailure(body);
            logger.LogInformation(
                "Supabase Auth rejected the password change with status {Status}, classified as {Reason}.",
                (int)response.StatusCode, reason);
            return AuthResult.Failure(reason);
        }

        return ParseSuccess(body, "user");
    }

    private async Task<AuthResult> PostCredentialsAsync(
        string path,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string body;

        try
        {
            response = await httpClient.PostAsJsonAsync(path, new { email, password }, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network failure must not surface as a wrong-password message.
            logger.LogError(ex, "Supabase Auth request to {Path} failed.", path);
            return AuthResult.Failure(AuthFailureReason.Unavailable);
        }

        if (!response.IsSuccessStatusCode)
        {
            var reason = ClassifyFailure(body);
            logger.LogInformation(
                "Supabase Auth rejected {Path} with status {Status}, classified as {Reason}.",
                path, (int)response.StatusCode, reason);
            return AuthResult.Failure(reason);
        }

        return ParseSuccess(body, path);
    }

    /// <summary>
    /// Maps GoTrue's error payload to a reason the UI can act on. GoTrue returns
    /// <c>{"code":400,"error_code":"invalid_credentials","msg":"..."}</c>; the machine-readable
    /// <c>error_code</c> is preferred over <c>msg</c>, which is prose and may change.
    /// </summary>
    private AuthFailureReason ClassifyFailure(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var errorCode = root.TryGetProperty("error_code", out var code) ? code.GetString() : null;
            errorCode ??= root.TryGetProperty("error", out var legacy) ? legacy.GetString() : null;

            return errorCode switch
            {
                "invalid_credentials" or "invalid_grant" => AuthFailureReason.InvalidCredentials,
                "user_already_exists" or "email_exists" => AuthFailureReason.AlreadyRegistered,
                "weak_password" => AuthFailureReason.WeakPassword,
                "same_password" => AuthFailureReason.SamePassword,
                "email_not_confirmed" => AuthFailureReason.EmailNotConfirmed,
                _ => AuthFailureReason.Unavailable
            };
        }
        catch (JsonException)
        {
            logger.LogWarning("Supabase Auth returned a non-JSON error payload.");
            return AuthFailureReason.Unavailable;
        }
    }

    /// <summary>
    /// Reads the user id and email out of a success payload, along with the access token when the
    /// response carries one. The token endpoint nests the user under <c>user</c>; signup returns
    /// it at the root when no session is issued, so both shapes are accepted.
    /// </summary>
    /// <remarks>
    /// The access token sits at the root beside <c>user</c>, never inside it. It is read but the
    /// refresh token is still dropped: nothing in this application refreshes a GoTrue session.
    /// </remarks>
    private AuthResult ParseSuccess(string body, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var user = root.TryGetProperty("user", out var nested) ? nested : root;

            if (user.TryGetProperty("id", out var idElement)
                && Guid.TryParse(idElement.GetString(), out var userId))
            {
                var email = user.TryGetProperty("email", out var emailElement)
                    ? emailElement.GetString() ?? string.Empty
                    : string.Empty;

                var accessToken = root.TryGetProperty("access_token", out var tokenElement)
                    ? tokenElement.GetString() ?? string.Empty
                    : string.Empty;

                return AuthResult.Success(userId, email, accessToken);
            }

            logger.LogError("Supabase Auth {Path} succeeded but returned no usable user id.", path);
            return AuthResult.Failure(AuthFailureReason.Unavailable);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Supabase Auth {Path} returned an unparseable success payload.", path);
            return AuthResult.Failure(AuthFailureReason.Unavailable);
        }
    }
}
