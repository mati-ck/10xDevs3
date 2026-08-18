namespace _10xnotes.Data.Entities;

/// <summary>
/// The two events the acceptance rate is computed from: <c>count(Saved) / count(Generated)</c>.
/// </summary>
/// <remarks>
/// Only these two, and deliberately no others. Both have to measure the same population or the
/// ratio is meaningless — see <see cref="NoteEvent"/> for exactly when each one is written.
/// </remarks>
public enum NoteEventKind
{
    /// <summary>
    /// A generation finished successfully and produced a draft the user could accept. The
    /// denominator.
    /// </summary>
    Generated = 0,

    /// <summary>
    /// The user accepted a freshly generated draft by saving it. The numerator.
    /// </summary>
    Saved = 1
}
