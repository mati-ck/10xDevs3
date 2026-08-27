using _10xnotes.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace _10xNotes.Tests;

/// <summary>
/// The SQLite in-memory options every test that runs an <c>ORDER BY</c> over a timestamp needs.
/// </summary>
/// <remarks>
/// SQLite has no date type, so its provider refuses to translate <c>ORDER BY</c> over a
/// <see cref="DateTimeOffset"/> at all — it cannot know whether the text it stored sorts
/// chronologically once offsets differ. Postgres has <c>timestamptz</c> and sorts it natively, so
/// this is a limitation of the test provider and not of the query: the production model, the LINQ
/// and the emitted <c>ORDER BY</c> are untouched.
/// <para>
/// The fix is the one the EF Core SQLite documentation prescribes — store the value as ticks, a
/// type SQLite can order — applied through a replaced <see cref="IModelCustomizer"/> so it exists
/// only for tests wired through here. Every timestamp this application writes is UTC
/// (<c>TimeProvider.GetUtcNow</c>), so ticks round-trip without losing anything.
/// </para>
/// <para>
/// All contexts sharing one in-memory connection must be built from these options, including the
/// one that runs <c>EnsureCreated</c> — the converter decides the column type, so a schema created
/// without it would be read with it.
/// </para>
/// </remarks>
internal static class SqliteTestContext
{
    public static DbContextOptions<AppDbContext> OptionsFor(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, TicksForDateTimeOffsetCustomizer>()
            .Options;

    /// <summary>Creates a context on <paramref name="connection"/> scoped to <paramref name="userId"/>.</summary>
    /// <remarks>
    /// Mirrors what <c>UserScopedDbContextFactory</c> does at runtime: create, then apply the
    /// current user. <see cref="Guid.Empty"/> stands for "nobody is signed in".
    /// </remarks>
    public static AppDbContext Create(SqliteConnection connection, Guid? userId) =>
        new(OptionsFor(connection)) { CurrentUserId = userId ?? Guid.Empty };

    /// <summary>
    /// Hands out contexts on a shared in-memory connection with no user applied — the shape
    /// <c>UserScopedDbContextFactory</c> expects underneath itself.
    /// </summary>
    public sealed class Factory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(OptionsFor(connection));
    }

    private sealed class TicksForDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        private static readonly ValueConverter<DateTimeOffset, long> ToTicks = new(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        private static readonly ValueConverter<DateTimeOffset?, long?> ToNullableTicks = new(
            value => value!.Value.UtcTicks,
            ticks => new DateTimeOffset(ticks!.Value, TimeSpan.Zero));

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            // Runs AppDbContext.OnModelCreating first, so this only re-types properties the real
            // model already declared — it can neither add nor hide one.
            base.Customize(modelBuilder, context);

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(ToTicks);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(ToNullableTicks);
                }
            }
        }
    }
}
