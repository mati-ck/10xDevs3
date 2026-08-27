using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <summary>
    /// Puts <c>NOT NULL</c> back on <c>source_materials.original_file_name</c>.
    /// </summary>
    /// <remarks>
    /// <c>AddSourceMaterialKind</c> dropped the constraint so a paste could store null. That was a
    /// mistake, and the review that found it says why: <c>Kind</c> already states that a row has no
    /// file behind it, so nullability carried the same fact twice — and it cost the one thing the
    /// column had to keep. The application version predating <c>Kind</c> maps this property as
    /// <c>IsRequired()</c>, and EF throws when it materialises a null into a required property
    /// (<c>InvalidOperationException: The data is NULL at ordinal N</c>). A Coolify rollback
    /// reverts code and not schema, so every null here is a row that breaks the material page for
    /// the version we would roll back to.
    /// <para>
    /// This is a forward fix rather than an edit to the migration that caused it: that one is
    /// already recorded in <c>__EFMigrationsHistory</c>, so rewriting it would change what a
    /// database built from scratch looks like without changing any database that exists.
    /// </para>
    /// <para>
    /// Hand-written rather than left as EF scaffolded it. EF's <c>AlterColumn</c> with
    /// <c>defaultValue: ""</c> leaves a <c>DEFAULT ''</c> on the column afterwards, which would
    /// quietly make the file name optional for every future insert — the same trap
    /// <c>AddSourceMaterialKind</c> avoided for <c>kind</c>, for the same reason. The backfill has
    /// to precede the constraint either way.
    /// </para>
    /// <para>
    /// Backward-compatible: a null becomes an empty string, which is what the pre-<c>Kind</c> code
    /// wrote for every row anyway, and the version currently deployed writes empty strings too.
    /// RLS is untouched — this alters a column, it does not create a table.
    /// </para>
    /// </remarks>
    public partial class RestoreOriginalFileNameNotNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Pastes written while the column was nullable. Empty is what the paste path
                -- stores now, so this is the same value, applied retroactively.
                UPDATE public.source_materials
                  SET original_file_name = ''
                  WHERE original_file_name IS NULL;

                ALTER TABLE public.source_materials
                  ALTER COLUMN original_file_name SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.source_materials
                  ALTER COLUMN original_file_name DROP NOT NULL;
                """);
        }
    }
}
