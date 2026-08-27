using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <summary>
    /// Teaches <c>source_materials</c> to describe a material that never came from a file.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than left as EF generated it, because EF's <c>AddColumn</c> with
    /// <c>defaultValue: ""</c> would stamp every existing row with an empty <c>kind</c> — a value
    /// no member of <c>SourceMaterialKind</c> maps to, which surfaces much later as an exception
    /// when the row is read back. The order below is the whole point: backfill first, constrain
    /// second.
    /// <para>
    /// Backward-compatible in both directions, which is a requirement rather than a nicety — a
    /// Coolify rollback does not reverse migrations. The application version from before this
    /// change runs fine against the schema after it: <c>kind</c> has a value in every row and the
    /// old code simply never selects it.
    /// </para>
    /// <para>
    /// <c>original_file_name</c> is deliberately left NOT NULL. An earlier draft dropped the
    /// constraint so a paste could store null, which read as the more honest model — but
    /// <c>Kind</c> already states that a row has no file, so the column would have carried the
    /// same fact a second time at a real cost: the pre-change model maps it as <c>IsRequired()</c>
    /// over a non-nullable string, and EF throws on materialising a null into it (verified:
    /// <c>InvalidOperationException: The data is NULL at ordinal N</c>). A rollback reverts code
    /// and not schema, so the first paste would have broken the material page for the version
    /// rolled back to. A paste stores an empty string instead, and this migration touches only
    /// <c>kind</c> — which is why <c>Down</c> has nothing to backfill.
    /// </para>
    /// <para>
    /// RLS is deliberately untouched: <c>source_materials</c> has had it enabled since
    /// <c>AddSourceMaterial</c>, and altering columns does not disturb it.
    /// </para>
    /// </remarks>
    public partial class AddSourceMaterialKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Added nullable so the backfill has somewhere to land. A NOT NULL column with a
                -- DEFAULT would have been one statement, but the default it needs ('MarkdownFile')
                -- would then linger on the column and quietly make the enum optional for every
                -- future insert.
                ALTER TABLE public.source_materials
                  ADD COLUMN kind character varying(20);

                -- Every row that exists today came from an uploaded file — which is exactly why
                -- MarkdownFile is the enum's zero value.
                UPDATE public.source_materials
                  SET kind = 'MarkdownFile'
                  WHERE kind IS NULL;

                ALTER TABLE public.source_materials
                  ALTER COLUMN kind SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Nothing to undo but the column: original_file_name was never touched, so there
                -- are no rows this migration made possible and no backfill to run before dropping.
                ALTER TABLE public.source_materials
                  DROP COLUMN kind;
                """);
        }
    }
}
