using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using _10xnotes.Data.Entities;
using _10xnotes.Generation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the generation contract: what reaches the model, and how every way the provider can fail
/// becomes a reason the UI can act on.
/// <para>
/// No network, no API key and no provider account are involved — a fake <see cref="IChatClient"/>
/// answers every call, so these run in CI, which has none of the three by design.
/// </para>
/// </summary>
public sealed class NoteGeneratorTests
{
    private const string ApiKey = "test-api-key";

    private static readonly SourceMaterial Material = new()
    {
        Id = new Guid("44444444-4444-4444-4444-444444444444"),
        Title = "Wykład o pamięci",
        Content = "Pamięć robocza ma ograniczoną pojemność."
    };

    [Fact]
    public async Task Fragments_arrive_in_order()
    {
        var generator = CreateGenerator(new FakeChatClient("Streszczenie", ": pamięć", " jest ograniczona."));

        var chunks = await CollectAsync(generator);

        Assert.Equal(
            ["Streszczenie", ": pamięć", " jest ograniczona."],
            chunks.Where(chunk => !chunk.IsFailure).Select(chunk => chunk.Text));
        Assert.DoesNotContain(chunks, chunk => chunk.IsFailure);
    }

    [Fact]
    public async Task The_system_prompt_and_the_material_both_reach_the_model()
    {
        var client = new FakeChatClient("notatka");

        await CollectAsync(CreateGenerator(client));

        var sent = client.Messages!;
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Equal(NotePrompt.System, sent[0].Text);

        Assert.Equal(ChatRole.User, sent[1].Role);
        Assert.Contains(Material.Title, sent[1].Text);
        Assert.Contains(Material.Content, sent[1].Text);
    }

    [Theory]
    [InlineData(429, GenerationFailure.RateLimited)]
    [InlineData(413, GenerationFailure.TooLong)]
    // Credentials and permissions are the operator's problem, never the user's — both land on the
    // neutral reason rather than telling the user something is wrong with their account.
    [InlineData(401, GenerationFailure.Unavailable)]
    [InlineData(403, GenerationFailure.Unavailable)]
    [InlineData(500, GenerationFailure.Unavailable)]
    [InlineData(400, GenerationFailure.Unavailable)]
    public async Task Provider_status_codes_map_to_classified_reasons(int status, GenerationFailure expected)
    {
        var generator = CreateGenerator(new FakeChatClient(Rejection(status, "Some English prose")));

        Assert.Equal(expected, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task An_oversized_prompt_reported_as_400_is_still_too_long()
    {
        // Not every OpenAI-compatible provider uses 413 for this, so the status alone would
        // misfile it as a generic outage and tell the user to "try again" forever.
        var generator = CreateGenerator(
            new FakeChatClient(Rejection(400, "This model's maximum context_length is 8192 tokens")));

        Assert.Equal(GenerationFailure.TooLong, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task A_network_failure_is_unavailable()
    {
        var generator = CreateGenerator(new FakeChatClient(new HttpRequestException("connection refused")));

        Assert.Equal(GenerationFailure.Unavailable, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task An_unclassified_exception_is_unavailable_rather_than_a_guess()
    {
        var generator = CreateGenerator(new FakeChatClient(new InvalidOperationException("something odd")));

        Assert.Equal(GenerationFailure.Unavailable, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task Exceeding_the_request_timeout_is_timed_out_not_cancelled()
    {
        var generator = CreateGenerator(new FakeChatClient(FakeChatClient.Never), timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(GenerationFailure.TimedOut, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task The_caller_walking_away_is_cancelled_not_timed_out()
    {
        // The distinction matters to the user: "timed out" invites a retry, "cancelled" is what
        // they just asked for. Both arrive as the same exception type from the same linked token,
        // so only the token state can tell them apart.
        var generator = CreateGenerator(new FakeChatClient(FakeChatClient.Never));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Equal(GenerationFailure.Cancelled, await FailureOfAsync(generator, caller.Token));
    }

    [Fact]
    public async Task A_stream_that_carries_no_text_is_empty()
    {
        var generator = CreateGenerator(new FakeChatClient());

        Assert.Equal(GenerationFailure.Empty, await FailureOfAsync(generator));
    }

    [Fact]
    public async Task A_missing_api_key_fails_without_calling_the_provider()
    {
        var client = new FakeChatClient("notatka");
        var generator = CreateGenerator(client, apiKey: "");

        Assert.Equal(GenerationFailure.Unavailable, await FailureOfAsync(generator));
        Assert.Null(client.Messages);
    }

    [Fact]
    public async Task No_provider_prose_reaches_the_caller()
    {
        const string prose = "Rate limit exceeded for gpt-oss on provider xyz";
        var generator = CreateGenerator(new FakeChatClient(Rejection(429, prose)));

        var chunks = await CollectAsync(generator);

        // The whole surface, not just the text property: the PRD requires Polish copy, so the
        // provider's English must have no path to the UI.
        Assert.DoesNotContain(prose, string.Join("|", chunks.Select(chunk => chunk.ToString())));
    }

    [Fact]
    public async Task A_failure_ends_the_stream()
    {
        var generator = CreateGenerator(new FakeChatClient(Rejection(429, "slow down")));

        var chunks = await CollectAsync(generator);

        Assert.Equal(GenerationFailure.RateLimited, Assert.Single(chunks).Failure);
    }

    private static NoteGenerator CreateGenerator(
        IChatClient chatClient,
        string apiKey = ApiKey,
        TimeSpan? timeout = null)
    {
        var options = Options.Create(new AiOptions
        {
            ApiKey = apiKey,
            RequestTimeout = timeout ?? TimeSpan.FromMinutes(2)
        });

        return new NoteGenerator(chatClient, options, NullLogger<NoteGenerator>.Instance);
    }

    private static async Task<List<GenerationChunk>> CollectAsync(
        NoteGenerator generator,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<GenerationChunk>();

        await foreach (var chunk in generator.GenerateAsync(Material, cancellationToken))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async Task<GenerationFailure?> FailureOfAsync(
        NoteGenerator generator,
        CancellationToken cancellationToken = default)
    {
        var chunks = await CollectAsync(generator, cancellationToken);

        return chunks[^1].Failure;
    }

    /// <summary>
    /// Builds the exception the OpenAI client raises for a non-success response, carrying the
    /// status the classifier keys off.
    /// </summary>
    private static ClientResultException Rejection(int status, string message) =>
        new(message, new StubPipelineResponse(status, message));

    /// <summary>
    /// Answers a planned sequence of fragments, throws a planned exception, or never returns.
    /// </summary>
    private sealed class FakeChatClient : IChatClient
    {
        /// <summary>Marker for a stream that hangs until a token cancels it.</summary>
        public static readonly string[] Never = ["\0never"];

        private readonly string[] _fragments;
        private readonly Exception? _exception;

        public FakeChatClient(params string[] fragments) => _fragments = fragments;

        public FakeChatClient(Exception exception)
        {
            _fragments = [];
            _exception = exception;
        }

        /// <summary>Null until the client is actually called — which is the point of one test.</summary>
        public List<ChatMessage>? Messages { get; private set; }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToList();
            return StreamAsync(cancellationToken);
        }

        private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                await Task.Yield();
                throw _exception;
            }

            if (ReferenceEquals(_fragments, Never))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }

            foreach (var fragment in _fragments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, fragment);
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Generation streams; it never asks for a whole response.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The minimum <see cref="PipelineResponse"/> needed to give a
    /// <see cref="ClientResultException"/> a status code.
    /// </summary>
    private sealed class StubPipelineResponse(int status, string body) : PipelineResponse
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => "Stubbed";

        public override Stream? ContentStream { get; set; } = new MemoryStream();

        public override BinaryData Content { get; } = BinaryData.FromString(body);

        protected override PipelineResponseHeaders HeadersCore { get; } = new StubHeaders();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Content);

        public override void Dispose() => ContentStream?.Dispose();

        private sealed class StubHeaders : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
                => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();

            public override bool TryGetValue(string name, out string? value)
            {
                value = null;
                return false;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }
        }
    }
}
