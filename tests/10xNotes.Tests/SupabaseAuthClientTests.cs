using System.Net;
using System.Text;
using System.Text.Json;
using _10xnotes.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the GoTrue contract: which endpoint each operation hits, that the project key travels
/// with it, and how an error response becomes a reason the UI can act on.
/// <para>
/// No network and no Supabase project are involved — a stubbed handler answers every request,
/// so these run in CI without credentials.
/// </para>
/// </summary>
public sealed class SupabaseAuthClientTests
{
    private const string AnonKey = "test-anon-key";
    private static readonly Guid UserId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task SignUp_posts_to_the_signup_endpoint_with_the_project_key()
    {
        var handler = new StubHandler(Ok($$"""{"id":"{{UserId}}","email":"ala@example.com"}"""));
        var client = CreateClient(handler);

        await client.SignUpAsync("ala@example.com", "haslo12345");

        Assert.Equal("https://project.supabase.co/auth/v1/signup", handler.RequestUri?.ToString());
        Assert.Equal(AnonKey, Assert.Single(handler.ApiKeyHeader!));
    }

    [Fact]
    public async Task SignIn_posts_to_the_password_grant_endpoint_with_the_project_key()
    {
        var handler = new StubHandler(Ok($$$"""{"user":{"id":"{{{UserId}}}","email":"ala@example.com"}}"""));
        var client = CreateClient(handler);

        await client.SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal(
            "https://project.supabase.co/auth/v1/token?grant_type=password",
            handler.RequestUri?.ToString());
        Assert.Equal(AnonKey, Assert.Single(handler.ApiKeyHeader!));
    }

    [Fact]
    public async Task Credentials_travel_in_the_body_not_the_query_string()
    {
        var handler = new StubHandler(Ok($$"""{"id":"{{UserId}}","email":"ala@example.com"}"""));
        var client = CreateClient(handler);

        await client.SignUpAsync("ala@example.com", "haslo12345");

        Assert.DoesNotContain("haslo12345", handler.RequestUri!.ToString());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("ala@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("haslo12345", body.RootElement.GetProperty("password").GetString());
    }

    [Theory]
    [InlineData("invalid_credentials", AuthFailureReason.InvalidCredentials)]
    [InlineData("invalid_grant", AuthFailureReason.InvalidCredentials)]
    [InlineData("user_already_exists", AuthFailureReason.AlreadyRegistered)]
    [InlineData("email_exists", AuthFailureReason.AlreadyRegistered)]
    [InlineData("weak_password", AuthFailureReason.WeakPassword)]
    [InlineData("same_password", AuthFailureReason.SamePassword)]
    [InlineData("email_not_confirmed", AuthFailureReason.EmailNotConfirmed)]
    // An unrecognised code must not be guessed at — anything unclassified is "Unavailable",
    // which the UI renders as a neutral message rather than a claim about the account.
    [InlineData("over_request_rate_limit", AuthFailureReason.Unavailable)]
    public async Task GoTrue_error_codes_map_to_classified_reasons(string errorCode, AuthFailureReason expected)
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                $$"""{"code":400,"error_code":"{{errorCode}}","msg":"Some English prose"}""",
                Encoding.UTF8,
                "application/json")
        });

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.FailureReason);
    }

    [Fact]
    public async Task Legacy_error_property_is_classified_too()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":"invalid_grant","error_description":"Invalid login credentials"}""",
                Encoding.UTF8,
                "application/json")
        });

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal(AuthFailureReason.InvalidCredentials, result.FailureReason);
    }

    [Fact]
    public async Task A_non_json_error_payload_does_not_throw()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html")
        });

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    [Fact]
    public async Task No_gotrue_prose_reaches_the_caller()
    {
        const string prose = "Invalid login credentials";
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                $$"""{"code":400,"error_code":"invalid_credentials","msg":"{{prose}}"}""",
                Encoding.UTF8,
                "application/json")
        });

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        // The whole public surface, not just a known property: the PRD requires Polish copy, so
        // GoTrue's English must have no path to the UI.
        Assert.DoesNotContain(prose, result.ToString());
        Assert.Equal(string.Empty, result.Email);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_not_wrong_password()
    {
        var handler = new StubHandler(new HttpRequestException("connection refused"));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    [Fact]
    public async Task A_timeout_is_unavailable_not_wrong_password()
    {
        var handler = new StubHandler(new TaskCanceledException("timed out"));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    [Fact]
    public async Task A_user_nested_under_user_is_read()
    {
        var handler = new StubHandler(Ok(
            $$$"""{"access_token":"jwt","user":{"id":"{{{UserId}}}","email":"ala@example.com"}}"""));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.True(result.Succeeded);
        Assert.Equal(UserId, result.UserId);
        Assert.Equal("ala@example.com", result.Email);
    }

    [Fact]
    public async Task A_user_at_the_root_is_read()
    {
        var handler = new StubHandler(Ok($$"""{"id":"{{UserId}}","email":"ala@example.com"}"""));

        var result = await CreateClient(handler).SignUpAsync("ala@example.com", "haslo12345");

        Assert.True(result.Succeeded);
        Assert.Equal(UserId, result.UserId);
    }

    // -- The access token ------------------------------------------------------------------

    /// <summary>
    /// The one GoTrue token this application keeps, and only for the length of a single method:
    /// <c>PUT /user</c> needs a bearer token and re-authenticating is the only way to obtain one.
    /// Before this, <c>ParseSuccess</c> read past <c>access_token</c> and dropped it.
    /// </summary>
    [Fact]
    public async Task The_password_grant_returns_its_access_token()
    {
        var handler = new StubHandler(Ok(
            $$$"""{"access_token":"jwt-abc","refresh_token":"r","user":{"id":"{{{UserId}}}","email":"ala@example.com"}}"""));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.True(result.Succeeded);
        Assert.Equal("jwt-abc", result.AccessToken);
    }

    /// <summary>
    /// Signup with email confirmation on returns a user and no session. That is a success, not a
    /// fault — reading the token must not turn it into one.
    /// </summary>
    [Fact]
    public async Task A_success_payload_without_a_token_is_still_a_success()
    {
        var handler = new StubHandler(Ok($$"""{"id":"{{UserId}}","email":"ala@example.com"}"""));

        var result = await CreateClient(handler).SignUpAsync("ala@example.com", "haslo12345");

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.AccessToken);
    }

    /// <summary>
    /// A record prints every property, and <c>ToString</c> is what a log statement or a debugger
    /// reaches by accident. A bearer token in a log line is a credential in a log line.
    /// </summary>
    [Fact]
    public async Task The_access_token_does_not_appear_in_the_results_string_form()
    {
        var handler = new StubHandler(Ok(
            $$$"""{"access_token":"jwt-abc","user":{"id":"{{{UserId}}}","email":"ala@example.com"}}"""));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.Equal("jwt-abc", result.AccessToken);
        Assert.DoesNotContain("jwt-abc", result.ToString());
    }

    [Fact]
    public async Task A_success_without_a_usable_id_fails_closed()
    {
        var handler = new StubHandler(Ok("""{"user":{"email":"ala@example.com"}}"""));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    [Fact]
    public async Task An_unparseable_success_payload_fails_closed()
    {
        var handler = new StubHandler(Ok("not json at all"));

        var result = await CreateClient(handler).SignInAsync("ala@example.com", "haslo12345");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    // -- Changing a password ------------------------------------------------------------------

    private const string CurrentPassword = "stare-haslo-123";
    private const string NewPassword = "nowe-haslo-456";

    private static HttpResponseMessage ReauthenticationOk(string token = "jwt-abc") => Ok(
        $$$"""{"access_token":"{{{token}}}","refresh_token":"r","user":{"id":"{{{UserId}}}","email":"ala@example.com"}}""");

    private static HttpResponseMessage UserOk() => Ok(
        $$"""{"id":"{{UserId}}","email":"ala@example.com"}""");

    /// <summary>
    /// Re-authenticate, then update — in that order. The order is the whole security property:
    /// the token endpoint is what proves the current password, so a change that reached
    /// <c>PUT /user</c> without it would let a stolen cookie set a new password.
    /// </summary>
    [Fact]
    public async Task Changing_a_password_reauthenticates_first_then_updates_the_user()
    {
        var handler = new SequenceHandler(ReauthenticationOk(), UserOk());

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.Requests.Count);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            "https://project.supabase.co/auth/v1/token?grant_type=password",
            handler.Requests[0].Uri?.ToString());

        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal("https://project.supabase.co/auth/v1/user", handler.Requests[1].Uri?.ToString());

        // The project key travels on both, exactly as Program.cs wires it.
        Assert.All(handler.Requests, request => Assert.Equal(AnonKey, Assert.Single(request.ApiKey!)));
    }

    /// <summary>
    /// GoTrue's requireAuthentication middleware reads this header; without it the update is
    /// refused no matter how the re-authentication went.
    /// </summary>
    [Fact]
    public async Task The_update_carries_the_bearer_token_the_reauthentication_returned()
    {
        var handler = new SequenceHandler(ReauthenticationOk("jwt-from-reauth"), UserOk());

        await CreateClient(handler).ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.Null(handler.Requests[0].Authorization);
        Assert.Equal("Bearer", handler.Requests[1].Authorization?.Scheme);
        Assert.Equal("jwt-from-reauth", handler.Requests[1].Authorization?.Parameter);
    }

    /// <summary>
    /// The case that makes proving the old password structural rather than merely intended: a
    /// wrong current password must stop the flow dead, not fall through to the update.
    /// </summary>
    [Fact]
    public async Task A_wrong_current_password_issues_no_update()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"code":400,"error_code":"invalid_credentials","msg":"Invalid login credentials"}""",
                    Encoding.UTF8,
                    "application/json")
            },
            UserOk());

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", "zle-haslo", NewPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.InvalidCredentials, result.FailureReason);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    /// <summary>
    /// PUT /user also accepts email and data. Sending either would change something nobody asked
    /// to change — an email in particular starts a confirmation flow this app cannot complete.
    /// </summary>
    [Fact]
    public async Task The_update_body_carries_only_the_new_password()
    {
        var handler = new SequenceHandler(ReauthenticationOk(), UserOk());

        await CreateClient(handler).ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);

        var only = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("password", only.Name);
        Assert.Equal(NewPassword, only.Value.GetString());

        // And the old password never travels on the update leg.
        Assert.DoesNotContain(CurrentPassword, handler.Requests[1].Body);
    }

    [Theory]
    [InlineData("weak_password", AuthFailureReason.WeakPassword)]
    [InlineData("same_password", AuthFailureReason.SamePassword)]
    public async Task The_update_rejection_is_classified_for_the_page(string errorCode, AuthFailureReason expected)
    {
        var handler = new SequenceHandler(
            ReauthenticationOk(),
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    $$"""{"code":422,"error_code":"{{errorCode}}","msg":"Some English prose"}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.FailureReason);
    }

    /// <summary>
    /// Fail closed rather than sending an unauthenticated PUT, which GoTrue would reject as if
    /// the password were bad — a message the user could not act on.
    /// </summary>
    [Fact]
    public async Task A_reauthentication_without_a_token_does_not_attempt_the_update()
    {
        var handler = new SequenceHandler(
            Ok($$$"""{"user":{"id":"{{{UserId}}}","email":"ala@example.com"}}"""),
            UserOk());

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_network_failure_on_the_update_is_unavailable()
    {
        var handler = new SequenceHandler(ReauthenticationOk(), new HttpRequestException("connection refused"));

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.Equal(AuthFailureReason.Unavailable, result.FailureReason);
    }

    /// <summary>
    /// The token exists to authorize one request and then stop existing. Returning it would make
    /// it something a caller could keep.
    /// </summary>
    [Fact]
    public async Task The_change_returns_no_token_to_its_caller()
    {
        var handler = new SequenceHandler(ReauthenticationOk("jwt-secret"), UserOk());

        var result = await CreateClient(handler)
            .ChangePasswordAsync("ala@example.com", CurrentPassword, NewPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.AccessToken);
        Assert.DoesNotContain("jwt-secret", result.ToString());
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>
    /// Builds the client exactly as <c>Program.cs</c> does — same base address shape and same
    /// <c>apikey</c> header — so a change to that wiring shows up as a failure here.
    /// </summary>
    private static SupabaseAuthClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://project.supabase.co/auth/v1/")
        };
        httpClient.DefaultRequestHeaders.Add("apikey", AnonKey);

        return new SupabaseAuthClient(httpClient, NullLogger<SupabaseAuthClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public StubHandler(HttpResponseMessage response) => _response = response;

        public StubHandler(Exception exception) => _exception = exception;

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        public IEnumerable<string>? ApiKeyHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            ApiKeyHeader = request.Headers.TryGetValues("apikey", out var values) ? values : null;

            return _exception is null ? _response! : throw _exception;
        }
    }

    /// <summary>
    /// Answers a scripted series of responses and records every request, so a two-leg flow can be
    /// asserted on in order — including the leg that must <em>not</em> happen.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="StubHandler"/> rather than replacing it: that one answers every
    /// request identically, which is exactly right for the single-call tests above and would
    /// hide an ordering mistake here.
    /// </remarks>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses;

        public SequenceHandler(params object[] responses) => _responses = new Queue<object>(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("apikey", out var key) ? key.ToList() : null,
                request.Headers.Authorization));

            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException(
                    $"Unexpected request {Requests.Count}: {request.Method} {request.RequestUri}");

            return next is Exception exception ? throw exception : (HttpResponseMessage)next;
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? Uri,
        string? Body,
        List<string>? ApiKey,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization);
}
