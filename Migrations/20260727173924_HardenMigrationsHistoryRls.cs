using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class HardenMigrationsHistoryRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core creates __EFMigrationsHistory itself, so it escaped the RLS hardening
            // applied to public.profiles in the initial migration — and Supabase grants
            // anon/authenticated full DML on tables in "public". The anon key is public by
            // design, so without this anyone could delete or forge rows in the migration
            // ledger and break the next deploy. No policies: the app's postgres role bypasses
            // RLS (rolbypassrls = true), so EF is unaffected.
            migrationBuilder.Sql("""
                ALTER TABLE public."__EFMigrationsHistory" ENABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op. The symmetric Down() would DISABLE ROW LEVEL SECURITY,
            // re-opening the migration ledger to the public anon key — the exact hole Up()
            // exists to close. Reverting this migration must not be a security regression,
            // so the hardening stays in place. Drop it by hand if it is ever really wanted.
        }
    }
}
