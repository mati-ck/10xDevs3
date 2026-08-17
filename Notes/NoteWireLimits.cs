namespace _10xnotes.Notes;

/// <summary>
/// How large a note may be <em>on the wire</em>, as opposed to how large it may be as text.
/// </summary>
/// <remarks>
/// The editor sends the whole note as a single SignalR hub invocation when the textarea loses
/// focus. SignalR's default inbound bound is 32 KB — exactly half of
/// <see cref="NoteValidator.MaxContentLength"/> — and exceeding it does not surface as an error a
/// component can catch: the connection is aborted, so the user gets a reconnect modal and loses a
/// note that was never saved. The application would be advertising a limit its own transport
/// refuses to carry.
/// <para>
/// So the transport bound is derived from the text bound rather than set independently, and this
/// is the only place either is converted into the other.
/// </para>
/// </remarks>
public static class NoteWireLimits
{
    /// <summary>
    /// Worst-case bytes on the wire per UTF-16 code unit of note text.
    /// </summary>
    /// <remarks>
    /// <see cref="NoteValidator.MaxContentLength"/> counts UTF-16 code units, not bytes, and one
    /// unit can cost far more than one byte by the time it reaches the hub: three bytes for a
    /// non-Latin BMP character encoded as UTF-8, and six for a control character escaped as
    /// <c>\uXXXX</c> in the JSON payload. Six is the ceiling of both.
    /// </remarks>
    private const int WorstCaseBytesPerChar = 6;

    /// <summary>
    /// Room for everything travelling beside the note text: the title, the hub protocol's own
    /// framing, and the method and argument names.
    /// </summary>
    private const int FramingHeadroom = 64 * 1024;

    /// <summary>
    /// The value <c>Program.cs</c> applies to the components hub. Removing that call silently
    /// restores the 32 KB default and reintroduces the data loss — <c>NoteWireLimitTests</c> pins
    /// the arithmetic, but only this constant being *used* keeps the guarantee.
    /// </summary>
    public const int MaximumReceiveMessageSize =
        (NoteValidator.MaxContentLength * WorstCaseBytesPerChar) + FramingHeadroom;
}
