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

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>
    /// Builds the client exactly as <c>Program.cs</c> does — same base address shape and same
    /// <c>apikey</c> header — so a change to that wiring shows up as a failure here.
    /// </summary>
    private static SupabaseAuthClient CreateClient(StubHandler handler)
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
}
