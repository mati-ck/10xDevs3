using _10xnotes.Time;

namespace _10xNotes.Tests;

/// <summary>
/// Pins the clock the user reads. The bug this closes was invisible in development — a machine
/// set to Polish time renders correctly with either implementation, and only the UTC container
/// in production shows the wrong hour.
/// </summary>
public sealed class AppTimeProviderTests
{
    private static readonly TimeProvider Warsaw = new AppTimeProvider();

    [Fact]
    public void The_display_time_zone_is_the_audiences_not_the_servers()
    {
        Assert.Equal("Europe/Warsaw", Warsaw.LocalTimeZone.Id);
    }

    [Fact]
    public void A_summer_instant_is_shown_two_hours_ahead_of_utc()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        var displayed = Warsaw.ToDisplayTime(instant);

        Assert.Equal(14, displayed.Hour);
        Assert.Equal(TimeSpan.FromHours(2), displayed.Offset);
    }

    [Fact]
    public void A_winter_instant_is_shown_one_hour_ahead_of_utc()
    {
        // The pair of these two is the point: a hardcoded +1 or +2 offset would pass one test
        // and fail the other, which is exactly the bug a fixed offset ships.
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var displayed = Warsaw.ToDisplayTime(instant);

        Assert.Equal(13, displayed.Hour);
        Assert.Equal(TimeSpan.FromHours(1), displayed.Offset);
    }

    [Fact]
    public void Conversion_preserves_the_instant()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(instant.UtcDateTime, Warsaw.ToDisplayTime(instant).UtcDateTime);
    }

    [Fact]
    public void An_instant_already_carrying_an_offset_is_converted_not_reinterpreted()
    {
        // A DateTimeOffset read back from Postgres carries its own offset. Reinterpreting rather
        // than converting would shift the moment itself.
        var instant = new DateTimeOffset(2026, 7, 1, 15, 0, 0, TimeSpan.FromHours(5));

        var displayed = Warsaw.ToDisplayTime(instant);

        Assert.Equal(12, displayed.Hour);
        Assert.Equal(instant.UtcDateTime, displayed.UtcDateTime);
    }

    [Fact]
    public void An_unknown_time_zone_id_fails_loudly_at_construction()
    {
        // Better a startup failure naming the bad id than a silent fallback to UTC that renders
        // wrong times forever.
        Assert.ThrowsAny<Exception>(() => new AppTimeProvider("Nowhere/Nothing"));
    }

    [Fact]
    public void A_blank_configured_id_falls_back_to_the_default()
    {
        Assert.Equal(AppTimeProvider.DefaultTimeZoneId, new AppTimeProvider("  ").LocalTimeZone.Id);
    }
}
