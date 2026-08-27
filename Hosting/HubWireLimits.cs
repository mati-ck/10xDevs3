using _10xnotes.Notes;
using _10xnotes.SourceMaterials;

namespace _10xnotes.Hosting;

/// <summary>
/// How large user text may be <em>on the wire</em>, as opposed to how large it may be as text.
/// </summary>
/// <remarks>
/// Two things cross this boundary as a single SignalR hub invocation: a note leaving the editor
/// when the textarea loses focus, and a pasted material leaving the add-material page the same
/// way. Both send their whole value in one frame, so the bound has to clear the larger of the two
/// — today the paste.
/// <para>
/// Exceeding <c>MaximumReceiveMessageSize</c> does not surface as an error a component can catch:
/// the connection is aborted, so the user gets a reconnect modal and loses text that was never
/// saved. That is how this went wrong the first time, in S-01c, with SignalR's untouched 32 KB
/// default against a 64 KB note limit.
/// </para>
/// <para>
/// This type was called <c>NoteWireLimits</c> and lived under <c>Notes/</c> while the note was the
/// only thing crossing. It was renamed and moved here — beside <c>Program.cs</c>'s own concerns
/// rather than beside either feature — precisely so the next person adding a large field does not
/// find it filed under someone else's feature and derive the bound from that feature alone. Add a
/// third large payload and it belongs in the <c>Math.Max</c> below, with a case in
/// <c>NoteWireLimitTests</c>.
/// </para>
/// </remarks>
public static class HubWireLimits
{
    /// <summary>
    /// Worst-case bytes on the wire per UTF-16 code unit of user text.
    /// </summary>
    /// <remarks>
    /// Both text limits count UTF-16 code units, not bytes, and one unit can cost far more than
    /// one byte by the time it reaches the hub: three bytes for a non-Latin BMP character encoded
    /// as UTF-8, and six for a character escaped as <c>\uXXXX</c>. Six is the ceiling of both.
    /// <para>
    /// Six has been challenged once and the challenge was wrong, so the reasoning is recorded here
    /// rather than left to be re-derived. The argument was that the components hub negotiates
    /// <c>blazorpack</c> (MessagePack, raw UTF-8), which would make three the true ceiling and this
    /// bound twice what it needs to be. Lowering it to three was measured in a browser on
    /// 2026-08-27: a paste of 131 072 <c>ż</c> characters — the advertised limit, 262 144 bytes of
    /// UTF-8, comfortably inside the 458 752 that three would have produced — did **not** reach the
    /// server. The circuit was torn down and silently resumed, the value never arrived, and the
    /// user got no message at all. At six the identical paste arrives and the reconnect modal never
    /// moves. Whatever the event-argument payload costs on this path, it is not the raw UTF-8
    /// length of the string, and measuring <c>IHubProtocol.GetMessageBytes</c> does not capture it.
    /// </para>
    /// <para>
    /// So: do not lower this number on the strength of an argument about the protocol. The only
    /// evidence that counts is a paste at <c>PasteValidator.MaxContentLength</c> reaching the
    /// server in a real browser, and no test in this project can produce it.
    /// </para>
    /// </remarks>
    private const int WorstCaseBytesPerChar = 6;

    /// <summary>
    /// Room for everything travelling beside the text: the title, the hub protocol's own framing,
    /// and the method and argument names.
    /// </summary>
    private const int FramingHeadroom = 64 * 1024;

    /// <summary>
    /// The value <c>Program.cs</c> applies to the components hub. Removing that call silently
    /// restores the 32 KB default and reintroduces the data loss — <c>NoteWireLimitTests</c> pins
    /// the arithmetic, but only this value being *used* keeps the guarantee.
    /// </summary>
    /// <remarks>
    /// <c>static readonly</c> rather than <c>const</c>: <see cref="Math.Max(int, int)"/> is not a
    /// constant expression. Nothing downstream needs a compile-time constant — <c>Program.cs</c>
    /// passes it to a method — so taking the larger of the two limits at runtime costs nothing and
    /// is what keeps the two from having to be compared by hand.
    /// </remarks>
    public static readonly int MaximumReceiveMessageSize =
        (Math.Max(NoteValidator.MaxContentLength, PasteValidator.MaxContentLength)
            * WorstCaseBytesPerChar)
        + FramingHeadroom;
}
