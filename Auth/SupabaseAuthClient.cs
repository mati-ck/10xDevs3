using System.Net.Http.Json;
using System.Text.Json;

namespace _10xnotes.Auth;

/// <summary>
/// Talks to Supabase Auth (GoTrue) over its REST API.
/// </summary>
/// <remarks>
/// Supabase is used purely as a credential store: we create and verify credentials here, then
/// mint our own cookie and discard GoTrue's tokens. That is why only two endpoints are needed
/// and why no SDK is taken on.
/// </remarks>
public sealed class SupabaseAuthClient(HttpClient httpClient, ILogger<SupabaseAuthClient> logger)
{
    public Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
        => PostCredentialsAsync("signup", email, password, cancellationToken);

    public Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
        => PostCredentialsAsync("token?grant_type=password", email, password, cancellationToken);

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
    /// Reads the user id and email out of a success payload. The token endpoint nests the user
    /// under <c>user</c>; signup returns it at the root when no session is issued, so both
    /// shapes are accepted.
    /// </summary>
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

                return AuthResult.Success(userId, email);
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
