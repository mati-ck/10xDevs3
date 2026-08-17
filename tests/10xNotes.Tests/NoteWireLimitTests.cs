using System.Text;
using System.Text.Json;
using _10xnotes.Notes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _10xNotes.Tests;

/// <summary>
/// Keeps the note's text limit and its transport limit from drifting apart.
/// </summary>
/// <remarks>
/// These two numbers live in different files and are set for different reasons, which is exactly
/// how they came to disagree: the validator allowed 64 KB, the app told the user so, and SignalR's
/// untouched 32 KB default silently aborted the circuit for anything larger — destroying a note
/// that had never been saved. A limit the application advertises must be one its transport can
/// carry, and that is an arithmetic property, so it belongs in a test rather than in a comment.
/// </remarks>
public sealed class NoteWireLimitTests
{
    /// <summary>
    /// The worst case a note can be by the time it reaches the hub: every UTF-16 code unit a
    /// control character, which the JSON payload escapes to six bytes each.
    /// </summary>
    [Fact]
    public void The_wire_limit_carries_the_largest_note_the_validator_accepts()
    {
        var worstCaseNote = new string('\u0001', NoteValidator.MaxContentLength);

        var payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(worstCaseNote));

        Assert.True(
            payloadBytes <= NoteWireLimits.MaximumReceiveMessageSize,
            $"A note at the validator's limit serializes to {payloadBytes} bytes, which exceeds the "
            + $"{NoteWireLimits.MaximumReceiveMessageSize}-byte hub limit. Saving it would abort the "
            + "circuit instead of returning an error, and the note would be lost.");
    }

    /// <summary>
    /// The same bound against realistic Polish text, which is where the real ceiling sits.
    /// </summary>
    [Fact]
    public void The_wire_limit_carries_a_full_note_of_polish_text()
    {
        // Every character a two-byte diacritic — heavier than any real note, lighter than the
        // pathological case above.
        var polishNote = new string('ż', NoteValidator.MaxContentLength);

        var payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(polishNote));

        Assert.True(payloadBytes <= NoteWireLimits.MaximumReceiveMessageSize);
    }

    /// <summary>
    /// Proves the fix is not a no-op: SignalR's own default is smaller than the notes this
    /// application accepts, so the explicit configuration in <c>Program.cs</c> is load-bearing.
    /// </summary>
    [Fact]
    public void SignalRs_default_would_not_carry_a_full_note()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRazorComponents().AddInteractiveServerComponents();

        var @default = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HubOptions>>().Value.MaximumReceiveMessageSize;

        Assert.NotNull(@default);
        Assert.True(
            @default < NoteValidator.MaxContentLength,
            "SignalR's default receive limit now exceeds the note limit on its own. If that is "
            + "genuinely true, this test and the AddHubOptions call in Program.cs can go — but "
            + "verify it rather than deleting them on the strength of this message.");
    }
}
