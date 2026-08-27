using _10xnotes.Data;
using Microsoft.EntityFrameworkCore;

namespace _10xnotes.SourceMaterials;

/// <summary>
/// Reads of source materials that belong outside a page — today, the materials list.
/// </summary>
/// <remarks>
/// Data comes through <see cref="UserScopedDbContextFactory"/>, never an injected context or
/// context factory: <c>DataAccessBoundaryTests</c> fails the build otherwise, and a context
/// obtained the other way has no current user, so every read comes back empty.
/// <para>
/// A new type rather than a method on something existing, and deliberately narrow: the list is
/// the only material read that has to be provable, because the project has no bUnit and a
/// component cannot be tested. The direct <c>db.SourceMaterials</c> queries on the import,
/// material and note pages stay where they are — moving them here would be a change without a
/// reason in a slice that does not touch them.
/// </para>
/// </remarks>
public sealed class SourceMaterialService(UserScopedDbContextFactory dbContextFactory)
{
    /// <summary>
    /// Every material the signed-in user has imported, newest first, with the id of its saved
    /// note when one exists.
    /// </summary>
    /// <remarks>
    /// Deliberately unbounded — no paging, no take. The PRD puts data volume at "small" for a
    /// single-user product; the cost, and the threshold at which it stops being acceptable, are
    /// named in the plan's Performance Considerations.
    /// </remarks>
    public async Task<IReadOnlyList<SourceMaterialListItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateAsync(cancellationToken);

        // A left join written as a group join with DefaultIfEmpty(), because the project has no
        // navigation properties anywhere — relationships are expressed by id and every read is a
        // deliberate query.
        //
        // No owner filter is written here, on either side. The global query filter in
        // AppDbContext applies to BOTH source_materials and notes, so neither half of the join
        // can reach another account; and the unique index on notes.source_material_id means the
        // join matches at most one note, so DefaultIfEmpty() cannot duplicate a material row.
        // Both guarantees are properties of the model, not of this query.
        //
        // The Select runs before materialisation, so the SQL never mentions the content columns.
        var rows =
            from material in db.SourceMaterials
            join note in db.Notes on material.Id equals note.SourceMaterialId into notesForMaterial
            from note in notesForMaterial.DefaultIfEmpty()
            // Id breaks the tie: created_at defaults to now() in Postgres, and two imports in the
            // same millisecond would otherwise come back in an order that can change per refresh.
            orderby material.CreatedAt descending, material.Id
            select new SourceMaterialListItem(
                material.Id,
                material.Title,
                material.OriginalFileName,
                material.CreatedAt,
                note == null ? null : (Guid?)note.Id);

        return await rows.ToListAsync(cancellationToken);
    }
}
