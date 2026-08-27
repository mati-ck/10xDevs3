using _10xnotes.Hosting;
using _10xnotes.Notes;
using _10xnotes.SourceMaterials;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _10xNotes.Tests;

/// <summary>
/// Keeps every text limit the application advertises from drifting away from the transport limit
/// underneath it — the note's 64 K characters and the paste's 128 K characters alike.
/// </summary>
/// <remarks>
/// These numbers live in different files and are set for different reasons, which is exactly how
/// they came to disagree the first time: the validator allowed 64 KB, the app told the user so, and
/// SignalR's untouched 32 KB default silently aborted the circuit for anything larger — destroying
/// a note that had never been saved. A limit the application advertises must be one its transport
/// can carry, and that is an arithmetic property, so it belongs in a test rather than in a comment.
/// <para>
/// Every measurement here goes through the protocol the components hub actually negotiates —
/// <c>blazorpack</c>, resolved from the same registration <c>Program.cs</c> builds — rather than
/// through a stand-in. Measuring JSON instead would pin the arithmetic to escape expansion the
/// real transport never performs, which is how a bound ends up twice the size it needs to be while
/// the test still passes. If Blazor ever changes what it negotiates, <see cref="Blazorpack"/>
/// fails loudly instead of quietly measuring the wrong thing.
/// </para>
/// <para>
/// Add a third large field to any page and it needs a case here too.
/// </para>
/// </remarks>
public sealed class HubWireLimitTests
{
    /// <summary>
    /// The protocol the components hub negotiates, resolved the way the application registers it.
    /// </summary>
    private static IHubProtocol Blazorpack()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRazorComponents().AddInteractiveServerComponents();

        var protocols = services.BuildServiceProvider().GetServices<IHubProtocol>().ToList();

        return protocols.SingleOrDefault(p => p.Name == "blazorpack")
            ?? throw new InvalidOperationException(
                "The components hub no longer registers a 'blazorpack' protocol; registered: "
                + string.Join(", ", protocols.Select(p => p.Name))
                + ". HubWireLimits.WorstCaseBytesPerChar is derived from blazorpack's raw-UTF-8 "
                + "string encoding — re-derive it for whatever replaced it before deleting this.");
    }

    /// <summary>
    /// Bytes this text costs on the wire as the single argument of a hub invocation — the shape
    /// every large field on these pages travels in.
    /// </summary>
    private static int WireBytes(string text) =>
        Blazorpack().GetMessageBytes(new InvocationMessage("Save", [text])).Length;

    /// <summary>
    /// The worst case a note can be by the time it reaches the hub: every UTF-16 code unit a
    /// three-byte BMP character, which is blazorpack's ceiling per code unit.
    /// </summary>
    [Fact]
    public void The_wire_limit_carries_the_largest_note_the_validator_accepts()
    {
        var worstCaseNote = new string('一', NoteValidator.MaxContentLength);

        var payloadBytes = WireBytes(worstCaseNote);

        Assert.True(
            payloadBytes <= HubWireLimits.MaximumReceiveMessageSize,
            $"A note at the validator's limit reaches the hub as {payloadBytes} bytes, which "
            + $"exceeds the {HubWireLimits.MaximumReceiveMessageSize}-byte hub limit. Saving it "
            + "would abort the circuit instead of returning an error, and the note would be lost.");
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

        Assert.True(WireBytes(polishNote) <= HubWireLimits.MaximumReceiveMessageSize);
    }

    /// <summary>
    /// The paste is the larger of the two payloads, so it — not the note — is what actually sets
    /// the bound. Without this case the paste limit could be raised again with nothing to catch it.
    /// </summary>
    [Fact]
    public void The_wire_limit_carries_the_largest_paste_the_validator_accepts()
    {
        var worstCasePaste = new string('一', PasteValidator.MaxContentLength);

        var payloadBytes = WireBytes(worstCasePaste);

        Assert.True(
            payloadBytes <= HubWireLimits.MaximumReceiveMessageSize,
            $"A paste at the validator's limit reaches the hub as {payloadBytes} bytes, which "
            + $"exceeds the {HubWireLimits.MaximumReceiveMessageSize}-byte hub limit. Saving it "
            + "would abort the circuit instead of returning an error, and the pasted material "
            + "would be lost.");
    }

    /// <summary>
    /// The same bound against realistic Polish text, which is what a user actually pastes.
    /// </summary>
    [Fact]
    public void The_wire_limit_carries_a_full_paste_of_polish_text()
    {
        var polishPaste = new string('ż', PasteValidator.MaxContentLength);

        Assert.True(WireBytes(polishPaste) <= HubWireLimits.MaximumReceiveMessageSize);
    }

    /// <summary>
    /// Proves the fix is not a no-op: SignalR's own default is smaller than the text this
    /// application accepts, so the explicit configuration in <c>Program.cs</c> is load-bearing.
    /// </summary>
    /// <remarks>
    /// Both sides of these comparisons are byte counts measured through the real protocol. An
    /// earlier version of this test compared the default against <c>MaxContentLength</c> directly —
    /// a byte limit against a character count — which happened to hold but was the exact unit
    /// conflation this file exists to guard against.
    /// </remarks>
    [Fact]
    public void SignalRs_default_would_not_carry_a_full_note_or_paste()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRazorComponents().AddInteractiveServerComponents();

        var @default = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HubOptions>>().Value.MaximumReceiveMessageSize;

        Assert.NotNull(@default);

        Assert.True(
            @default < WireBytes(new string('ż', NoteValidator.MaxContentLength)),
            "SignalR's default receive limit now carries a full note on its own. If that is "
            + "genuinely true, this test and the AddHubOptions call in Program.cs can go — but "
            + "verify it rather than deleting them on the strength of this message.");

        Assert.True(
            @default < WireBytes(new string('ż', PasteValidator.MaxContentLength)),
            "SignalR's default receive limit now carries a full paste on its own — see the note "
            + "assertion above before acting on this.");
    }
}
