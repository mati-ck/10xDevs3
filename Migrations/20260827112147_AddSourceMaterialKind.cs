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
    /// old code simply never selects it, and <c>original_file_name</c> becomes nullable while the
    /// old code never writes null.
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

                -- The other half: a paste has no file behind it, so the name has to be allowed to
                -- be absent. No backfill needed in this direction.
                ALTER TABLE public.source_materials
                  ALTER COLUMN original_file_name DROP NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Rows created by the paste path carry a null file name, which is data this very
                -- migration made possible. Restoring NOT NULL without filling them first would
                -- make the rollback fail on exactly the rows the feature produced.
                UPDATE public.source_materials
                  SET original_file_name = ''
                  WHERE original_file_name IS NULL;

                ALTER TABLE public.source_materials
                  ALTER COLUMN original_file_name SET NOT NULL;

                ALTER TABLE public.source_materials
                  DROP COLUMN kind;
                """);
        }
    }
}
