using _10xnotes.Data;
using _10xnotes.Data.Entities;
using _10xnotes.Generation;
using _10xnotes.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _10xNotes.Tests;

/// <summary>
/// Proves the daily allowance actually holds: it is per user, it refuses past the limit, and it
/// turns over on the user's midnight rather than UTC's.
/// <para>
/// SQLite in-memory, like <see cref="OwnerScopingTests"/> — query filters, the unique index and
/// change tracking behave identically across providers. The Postgres-only parts (the auth.users
/// FK, RLS) are verified against the real database.
/// </para>
/// </summary>
public sealed class GenerationQuotaTests : IDisposable
{
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    /// <summary>Mid-afternoon in Warsaw, comfortably inside one calendar day in both zones.</summary>
    private static readonly DateTimeOffset Afternoon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public GenerationQuotaTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var schema = CreateContext(UserA);
        schema.Database.EnsureCreated();
    }

    [Fact]
    public async Task The_first_generation_of_the_day_is_allowed()
    {
        var service = CreateService(UserA, limit: 3);

        Assert.True(await service.TryReserveAsync());
    }

    [Fact]
    public async Task Reservations_are_allowed_up_to_the_limit_and_refused_after()
    {
        var service = CreateService(UserA, limit: 3);

        Assert.True(await service.TryReserveAsync());
        Assert.True(await service.TryReserveAsync());
        Assert.True(await service.TryReserveAsync());
        Assert.False(await service.TryReserveAsync());
        Assert.False(await service.TryReserveAsync());
    }

    [Fact]
    public async Task One_users_spending_does_not_touch_another_users_allowance()
    {
        var ala = CreateService(UserA, limit: 1);
        var bartek = CreateService(UserB, limit: 1);

        Assert.True(await ala.TryReserveAsync());
        Assert.False(await ala.TryReserveAsync());

        // The PRD privacy guardrail read from the other side: Bartek's allowance is untouched by
        // Ala having spent hers, which only holds because the ledger is owner-scoped.
        Assert.True(await bartek.TryReserveAsync());
    }

    [Fact]
    public async Task A_users_ledger_row_is_invisible_to_another_user()
    {
        await CreateService(UserA, limit: 5).TryReserveAsync();

        using var asUserB = CreateContext(UserB);

        Assert.Empty(asUserB.GenerationQuotas.ToList());
    }

    [Fact]
    public async Task The_allowance_resets_the_next_day()
    {
        var clock = new FixedClock(Afternoon);
        var service = CreateService(UserA, limit: 1, clock: clock);

        Assert.True(await service.TryReserveAsync());
        Assert.False(await service.TryReserveAsync());

        clock.Now = Afternoon.AddDays(1);

        Assert.True(await service.TryReserveAsync());
    }

    [Fact]
    public async Task The_day_turns_over_at_the_users_midnight_not_at_UTC_midnight()
    {
        // 22:30 UTC on the 17th is already 00:30 on the 18th in Warsaw (summer, UTC+2). Keying
        // the ledger on UTC would hand the user a second allowance an hour and a half early, and
        // then cut them off mid-evening the following day.
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 17, 22, 30, 0, TimeSpan.Zero));
        var service = CreateService(UserA, limit: 5, clock: clock);

        await service.TryReserveAsync();

        using var context = CreateContext(UserA);
        Assert.Equal(new DateOnly(2026, 8, 18), context.GenerationQuotas.Single().UsageDate);
    }

    [Fact]
    public async Task Spending_accumulates_on_one_row_per_day()
    {
        var service = CreateService(UserA, limit: 5);

        await service.TryReserveAsync();
        await service.TryReserveAsync();

        using var context = CreateContext(UserA);
        var quota = Assert.Single(context.GenerationQuotas);
        Assert.Equal(2, quota.Count);
        Assert.Equal(UserA, quota.OwnerId);
    }

    [Fact]
    public async Task A_limit_of_zero_refuses_everything()
    {
        // Guards the boundary from the other side: an operator setting the limit to 0 to switch
        // generation off must not get "one free call" from the create-the-first-row path.
        var service = CreateService(UserA, limit: 0);

        Assert.False(await service.TryReserveAsync());
    }

    [Fact]
    public async Task Reserving_without_an_authenticated_user_is_refused()
    {
        var service = CreateService(userId: null, limit: 5);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TryReserveAsync());
    }

    private GenerationQuotaService CreateService(Guid? userId, int limit, TimeProvider? clock = null)
    {
        var options = Options.Create(new AiOptions { DailyGenerationLimit = limit });

        return new GenerationQuotaService(
            CreateFactory(userId),
            clock ?? new FixedClock(Afternoon),
            options,
            NullLogger<GenerationQuotaService>.Instance);
    }

    /// <summary>
    /// A real <see cref="UserScopedDbContextFactory"/> over the in-memory connection — the service
    /// under test must go through the sanctioned seam, so the test wires the seam rather than
    /// bypassing it.
    /// </summary>
    private UserScopedDbContextFactory CreateFactory(Guid? userId) =>
        new(new SqliteContextFactory(_connection), new StubCurrentUserAccessor(userId));

    private AppDbContext CreateContext(Guid? userId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options) { CurrentUserId = userId ?? Guid.Empty };
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Hands out contexts on the shared in-memory connection, with no user applied.</summary>
    private sealed class SqliteContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    /// <summary>A clock the test moves by hand, so "tomorrow" does not require waiting.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        /// <summary>The audience's zone, matching <see cref="AppTimeProvider"/>.</summary>
        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById(AppTimeProvider.DefaultTimeZoneId);
    }
}
