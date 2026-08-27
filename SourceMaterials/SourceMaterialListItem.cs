using _10xnotes.Data.Entities;

namespace _10xnotes.SourceMaterials;

/// <summary>
/// One row of the materials list, plus the one thing the material itself does not know: whether
/// a note has already been saved for it.
/// </summary>
/// <remarks>
/// A projection for the same reason <see cref="_10xnotes.Notes.NoteListItem"/> is one:
/// <c>SourceMaterial.Content</c> is an unbounded Postgres <c>text</c> column whose real ceiling
/// is the 128 KB import limit, and the list has no use for a single byte of it.
/// <para>
/// <see cref="NoteId"/> carries both halves of "has a note / has no note": <c>null</c> means
/// there is none, a value is the id to open. The note's title is not carried — it is derived
/// from this material's title at generation time, so it would almost always repeat it.
/// </para>
/// <para>
/// <see cref="Kind"/> is carried even though <see cref="OriginalFileName"/> being <c>null</c>
/// would usually coincide with it, because the row has to state its provenance rather than let
/// the page guess: an absent file name is evidence of two different things, and only one of them
/// should change what the list says. Same reasoning, and same branch, as
/// <c>Components/Pages/Materials/Detail.razor</c>.
/// </para>
/// </remarks>
/// <param name="Id">The material's key, and the target of <c>/materials/{id}</c>.</param>
/// <param name="Title">The title the user gave the material at import.</param>
/// <param name="Kind">Where the material came from — read this, never infer it from <paramref name="OriginalFileName"/>.</param>
/// <param name="OriginalFileName">The file name a material came from; empty for a paste.</param>
/// <param name="CreatedAt">When the material was added — what the list sorts by.</param>
/// <param name="NoteId">The saved note for this material, or <c>null</c> when there is none.</param>
public sealed record SourceMaterialListItem(
    Guid Id,
    string Title,
    SourceMaterialKind Kind,
    string OriginalFileName,
    DateTimeOffset CreatedAt,
    Guid? NoteId);
