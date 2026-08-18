namespace _10xnotes.Generation;

/// <summary>
/// One element of a generation stream: either a fragment of the note, or the reason the stream
/// ended badly. Never both.
/// </summary>
/// <remarks>
/// A failure is terminal — nothing follows it in the stream.
/// </remarks>
public readonly record struct GenerationChunk(string? Text, GenerationFailure? Failure)
{
    public static GenerationChunk Content(string text) => new(text, null);

    public static GenerationChunk Failed(GenerationFailure failure) => new(null, failure);

    /// <summary>True when this chunk ends the stream.</summary>
    public bool IsFailure => Failure.HasValue;
}
