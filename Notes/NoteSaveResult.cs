using _10xnotes.Data.Entities;

namespace _10xnotes.Notes;

/// <summary>Why a note could not be saved, in terms the UI can act on.</summary>
/// <remarks>
/// One enum rather than two, even though the first four members restate
/// <see cref="NoteValidationFailure"/>: the page has one save button and should have one
/// exhaustive switch behind it, not a validation switch nested inside a persistence switch.
/// </remarks>
public enum NoteSaveFailure
{
    None = 0,

    TitleEmpty,
    TitleTooLong,
    ContentEmpty,
    ContentTooLong,

    /// <summary>
    /// The material or the note is gone, or belongs to somebody else. The two are one member on
    /// purpose — telling them apart would let anyone probe which ids exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// Two saves for the same material collided and the retry lost as well. Rare enough to need
    /// only "try again", but distinct from the others because nothing the user typed is wrong.
    /// </summary>
    Conflict
}

/// <summary>
/// Outcome of saving a note.
/// </summary>
/// <remarks>
/// A typed result rather than an exception, following <c>GenerationQuotaService</c>: the S-01b
/// implementation review found that an exception escaping a method that otherwise returns a
/// result lands in the page's catch-all branch, which replaces the specific reason with a generic
/// one exactly when the specific reason was the point.
/// </remarks>
public sealed record NoteSaveResult
{
    private NoteSaveResult() { }

    public bool Succeeded { get; private init; }

    /// <summary>The saved note. Non-null only when <see cref="Succeeded"/>.</summary>
    public Note? Note { get; private init; }

    public NoteSaveFailure FailureReason { get; private init; }

    public static NoteSaveResult Success(Note note) =>
        new() { Succeeded = true, Note = note };

    public static NoteSaveResult Failure(NoteSaveFailure reason) =>
        new() { Succeeded = false, FailureReason = reason };

    /// <summary>Lifts a validation rejection into the enum the page switches on.</summary>
    public static NoteSaveResult FromValidation(NoteValidationFailure reason) =>
        Failure(reason switch
        {
            NoteValidationFailure.TitleEmpty => NoteSaveFailure.TitleEmpty,
            NoteValidationFailure.TitleTooLong => NoteSaveFailure.TitleTooLong,
            NoteValidationFailure.ContentEmpty => NoteSaveFailure.ContentEmpty,
            NoteValidationFailure.ContentTooLong => NoteSaveFailure.ContentTooLong,
            // Unreachable while every member is mapped, and deliberately not silent if one is
            // ever added: "try again" is wrong but honest, whereas reporting success would save
            // something no rule approved.
            _ => NoteSaveFailure.Conflict
        });
}
