using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _10xnotes.Migrations
{
    /// <inheritdoc />
    public partial class AddHandleNewUserTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarantees every account has a profiles row, whatever creates the account —
            // including the Supabase dashboard, which never runs our C# code.
            //
            // The function must live in `public`: verified on this project that the postgres
            // role has TRIGGER privilege on auth.users but cannot CREATE in the auth schema.
            //
            // SECURITY DEFINER lets it insert past the deny-all RLS on public.profiles.
            // `SET search_path` is not optional on a SECURITY DEFINER function — without it the
            // function is vulnerable to search-path manipulation by the caller.
            //
            // ON CONFLICT DO NOTHING makes it idempotent against the unique owner_id index.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.handle_new_user()
                RETURNS trigger
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                BEGIN
                  INSERT INTO public.profiles (owner_id)
                  VALUES (NEW.id)
                  ON CONFLICT (owner_id) DO NOTHING;
                  RETURN NEW;
                END;
                $$;

                DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;

                CREATE TRIGGER on_auth_user_created
                  AFTER INSERT ON auth.users
                  FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
                DROP FUNCTION IF EXISTS public.handle_new_user();
                """);
        }
    }
}
