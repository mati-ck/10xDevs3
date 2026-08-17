using System.ClientModel;
using System.Runtime.CompilerServices;
using _10xnotes.Data.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace _10xnotes.Generation;

/// <summary>
/// Turns a source material into a stream of note fragments.
/// </summary>
/// <remarks>
/// The only type in the application that knows an AI provider exists. Everything above it sees
/// <see cref="GenerationChunk"/> and <see cref="GenerationFailure"/>, which is what lets the page
/// be written — and the provider be swapped — without either knowing about the other.
/// </remarks>
public sealed class NoteGenerator(
    IChatClient chatClient,
    IOptions<AiOptions> options,
    ILogger<NoteGenerator> logger)
{
    /// <summary>
    /// Low but not zero. Summarising rewards faithfulness over invention, and pinning the value
    /// keeps the 75%-acceptance measurement from drifting with sampling noise.
    /// </summary>
    private static readonly ChatOptions ChatOptions = new() { Temperature = 0.3f };

    private readonly AiOptions _options = options.Value;

    /// <summary>
    /// Whether a generation can even be attempted, i.e. whether an API key is configured.
    /// </summary>
    /// <remarks>
    /// Exposed so the caller can refuse *before* spending a quota slot. Reserving first is right
    /// for a real attempt — a timeout still bills for the tokens it produced — but a missing key
    /// is knowably free, and burning a user's whole daily allowance on a misconfiguration would
    /// then tell them to come back tomorrow.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// Streams the note for <paramref name="material"/>, or a single terminal failure.
    /// </summary>
    /// <remarks>
    /// Failure travels as the last element of the stream rather than as an exception. The caller
    /// consumes this with <c>await foreach</c>, and C# forbids <c>yield return</c> inside a block
    /// with a <c>catch</c> — so an exception-based contract would force every caller to wrap the
    /// loop itself and hand-classify what it caught, which is exactly the duplication this type
    /// exists to prevent.
    /// </remarks>
    public async IAsyncEnumerable<GenerationChunk> GenerateAsync(
        SourceMaterial material,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!IsConfigured)
        {
            // Fail before spending a round-trip on a request that cannot succeed. Logged loudly
            // because this is an operator mistake, not a user one — the user just sees the same
            // neutral "unavailable" message as for any other outage.
            logger.LogError(
                "Ai:ApiKey is not configured, so no note can be generated. Set Ai__ApiKey.");
            yield return GenerationChunk.Failed(GenerationFailure.Unavailable);
            yield break;
        }

        using var timeout = new CancellationTokenSource(_options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        // Deferred: creating the enumerator issues no request, so nothing here can throw. The
        // first MoveNextAsync below is what opens the connection.
        await using var updates = chatClient
            .GetStreamingResponseAsync(NotePrompt.BuildMessages(material), ChatOptions, linked.Token)
            .GetAsyncEnumerator(linked.Token);

        var producedText = false;

        while (true)
        {
            GenerationFailure? failure = null;
            var moved = false;
            string? text = null;

            try
            {
                moved = await updates.MoveNextAsync();

                if (moved)
                {
                    text = updates.Current.Text;
                }
            }
            catch (Exception exception)
            {
                failure = Classify(exception, cancellationToken, timeout);
            }

            if (failure is not null)
            {
                yield return GenerationChunk.Failed(failure.Value);
                yield break;
            }

            if (!moved)
            {
                break;
            }

            if (!string.IsNullOrEmpty(text))
            {
                producedText = true;
                yield return GenerationChunk.Content(text);
            }
        }

        if (!producedText)
        {
            // A clean stream that carried no text is a failure the user must be told about —
            // otherwise the panel just sits empty and looks like the button did nothing.
            logger.LogWarning("The model returned no text for material {MaterialId}.", material.Id);
            yield return GenerationChunk.Failed(GenerationFailure.Empty);
        }
    }

    /// <summary>
    /// Maps a provider exception onto a reason the UI can act on, logging the detail rather than
    /// returning it.
    /// </summary>
    /// <remarks>
    /// The cancellation branch cannot key off the exception type: a timeout and a user walking
    /// away both surface as <see cref="OperationCanceledException"/> from the same linked token.
    /// Only the token state tells them apart, and the caller's token is checked first — if the
    /// user left, that is the true story regardless of what else fired.
    /// </remarks>
    private GenerationFailure Classify(
        Exception exception,
        CancellationToken callerToken,
        CancellationTokenSource timeout)
    {
        switch (exception)
        {
            case OperationCanceledException when callerToken.IsCancellationRequested:
                return GenerationFailure.Cancelled;

            case OperationCanceledException when timeout.IsCancellationRequested:
                logger.LogWarning(
                    "Generation exceeded the {Timeout} budget.", _options.RequestTimeout);
                return GenerationFailure.TimedOut;

            case OperationCanceledException:
                return GenerationFailure.Cancelled;

            case ClientResultException clientResult:
                var failure = ClassifyStatus(clientResult);
                // Status and a bounded excerpt rather than the exception object: the SDK embeds the
                // provider's whole response body in its message, and some providers echo prompt
                // fragments back in validation or moderation errors — which would put the user's
                // own material into application logs, against the PRD privacy guardrail.
                logger.LogError(
                    "The AI provider rejected the request with status {Status}, classified as {Failure}. Excerpt: {Excerpt}",
                    clientResult.Status,
                    failure,
                    Excerpt(clientResult.Message));
                return failure;

            case HttpRequestException:
                logger.LogError(exception, "The AI provider could not be reached.");
                return GenerationFailure.Unavailable;

            default:
                // Never guessed at: anything unrecognised is neutral rather than a claim about
                // what the user did wrong.
                logger.LogError(exception, "Generation failed for an unclassified reason.");
                return GenerationFailure.Unavailable;
        }
    }

    private static GenerationFailure ClassifyStatus(ClientResultException exception) => exception.Status switch
    {
        429 => GenerationFailure.RateLimited,
        413 => GenerationFailure.TooLong,
        // Some OpenAI-compatible providers report an oversized prompt as a 400 rather than a 413,
        // so the status alone is not enough to tell "too long" from "malformed".
        400 when MentionsContextLength(exception.Message) => GenerationFailure.TooLong,
        _ => GenerationFailure.Unavailable
    };

    /// <summary>
    /// Enough of a provider error to diagnose it, capped so an echoed prompt cannot land in the
    /// log wholesale. Error codes and reasons sit at the front of these payloads; user material,
    /// when it appears at all, follows.
    /// </summary>
    private static string Excerpt(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        const int maxLength = 200;

        return message.Length <= maxLength ? message : message[..maxLength] + "…";
    }

    private static bool MentionsContextLength(string? message) =>
        message is not null
        && (message.Contains("context_length", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context length", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too long", StringComparison.OrdinalIgnoreCase));
}
