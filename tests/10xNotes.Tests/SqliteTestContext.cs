using _10xnotes.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

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
/// <para>
/// What these tests therefore prove is that the LINQ expresses the intended order, not that Npgsql
/// translates it; nothing in this repo proves the latter, and the plan accepted that. Three places
/// where ticks and <c>timestamptz</c> would disagree, none of them reachable today:
/// <list type="bullet">
/// <item>Precision — a tick is 100 ns and <c>timestamptz</c> is 1 µs, so rows 100-900 ns apart sort
/// strictly here and tie in Postgres. Every case here uses hour-scale gaps or exact equality, and
/// the <c>Id</c> tie-break keeps even a collapsed pair deterministic.</item>
/// <item>Nulls — the converter handles <c>DateTimeOffset?</c>, but no entity has a nullable
/// timestamp, so that branch is dead. If one is added and ordered by, Postgres puts nulls first on
/// <c>DESC</c> and SQLite puts them last; nothing here would flag the inversion.</item>
/// <item>Offsets — this converter accepts any <c>Offset</c>, while Npgsql *throws* when writing a
/// non-zero one to <c>timestamptz</c>. The UTC-only rule above is enforced by production, not by
/// this harness, so a future non-UTC write path would be green here and fail there.</item>
/// </list>
/// </para>
/// <para>
/// Not every SQLite test in this project comes through here: <c>CurrentUserAccessorTests</c>,
/// <c>GenerationQuotaTests</c>, <c>GenerationQuotaConcurrencyTests</c> and <c>OwnerScopingTests</c>
/// still build their own options, and so store timestamps as TEXT rather than as ticks. Nothing is
/// broken by that — each owns its connection, so no schema meets the wrong converter — but adding
/// an ordering assertion to any of them will hit the translation failure described above. Fold it
/// over rather than solving it a second time.
/// </para>
/// </remarks>
internal static class SqliteTestContext
{
    /// <param name="log">
    /// Receives the provider's log lines when supplied. Only <c>ProjectionGuardTests</c> uses it,
    /// to read back the SQL a service actually emitted rather than trusting the LINQ to have
    /// stayed a projection.
    /// </param>
    public static DbContextOptions<AppDbContext> OptionsFor(
        SqliteConnection connection,
        Action<string>? log = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, TicksForDateTimeOffsetCustomizer>();

        if (log is not null)
        {
            builder = builder.LogTo(log, [DbLoggerCategory.Database.Command.Name], LogLevel.Information);
        }

        return builder.Options;
    }

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
    public sealed class Factory(SqliteConnection connection, Action<string>? log = null)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(OptionsFor(connection, log));
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
