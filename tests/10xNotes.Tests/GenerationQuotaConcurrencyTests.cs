using _10xnotes.Data;
using _10xnotes.Generation;
using _10xnotes.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _10xNotes.Tests;

/// <summary>
/// Proves the daily allowance survives concurrent requests from one user — two browser tabs, or a
/// double-click that beats the page's own guard.
/// <para>
/// Separate from <see cref="GenerationQuotaTests"/> because it needs a different harness: those
/// tests share one <see cref="SqliteConnection"/>, which serializes everything and so cannot
/// exhibit a race at all. Here every context gets its own connection onto a shared-cache in-memory
/// database, so the reservations genuinely overlap.
/// </para>
/// <para>
/// The bug this pins: reading the row, checking the count in C#, then writing count+1 lets two
/// requests both read 4, both pass a limit of 5, and both write 5 — with no exception raised,
/// because each UPDATE matches its row. <c>OwnerId</c> is the entity's only concurrency token and
/// it never changes, so it can never detect that conflict. The fix moves the limit test into the
/// UPDATE's WHERE clause; these tests fail against the read-modify-write version.
/// </para>
/// </summary>
public sealed class GenerationQuotaConcurrencyTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Afternoon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString =
        $"Data Source=quota-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    /// <summary>A shared-cache in-memory database lives only while a connection to it is open.</summary>
    private readonly SqliteConnection _keepAlive;

    public GenerationQuotaConcurrencyTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Concurrent_reservations_never_exceed_the_limit(int limit)
    {
        var service = CreateService(UserA, limit);

        // More attempts than slots, all in flight together, starting from an empty ledger — so the
        // day's first-insert race and the increment race both get exercised.
        var attempts = Enumerable
            .Range(0, limit + 5)
            .Select(_ => Task.Run(() => service.TryReserveAsync()));

        var granted = await Task.WhenAll(attempts);

        Assert.Equal(limit, granted.Count(wasGranted => wasGranted));

        using var verify = CreateContext(UserA);
        Assert.Equal(limit, verify.GenerationQuotas.Single().Count);
    }

    [Fact]
    public async Task Concurrent_first_reservations_of_the_day_produce_one_row()
    {
        // The unique index on (owner_id, usage_date) is what makes this safe; without the retry
        // around it, the loser of the insert race would surface as an error to the user rather
        // than quietly incrementing the winner's row.
        var service = CreateService(UserA, limit: 10);

        var granted = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => service.TryReserveAsync())));

        Assert.All(granted, Assert.True);

        using var verify = CreateContext(UserA);
        var quota = Assert.Single(verify.GenerationQuotas);
        Assert.Equal(4, quota.Count);
    }

    private GenerationQuotaService CreateService(Guid userId, int limit) =>
        new(
            new UserScopedDbContextFactory(
                new PerCallSqliteContextFactory(_connectionString),
                new StubCurrentUserAccessor(userId)),
            new FixedClock(Afternoon),
            Options.Create(new AiOptions { DailyGenerationLimit = limit }),
            NullLogger<GenerationQuotaService>.Instance);

    private AppDbContext CreateContext(Guid userId) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options)
        {
            CurrentUserId = userId
        };

    public void Dispose() => _keepAlive.Dispose();

    /// <summary>
    /// Hands every context its own connection, which is what allows two reservations to be in
    /// flight at once — the shared-connection harness the other quota tests use cannot.
    /// </summary>
    private sealed class PerCallSqliteContextFactory(string connectionString)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById(AppTimeProvider.DefaultTimeZoneId);
    }
}
