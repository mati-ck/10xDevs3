namespace _10xnotes.Time;

public static class TimeProviderExtensions
{
    /// <summary>
    /// Converts a stored instant into the clock the user reads.
    /// </summary>
    /// <remarks>
    /// The one call every view should make instead of <c>DateTimeOffset.ToLocalTime()</c>, which
    /// resolves against the server's timezone rather than the audience's. Named for the intent
    /// ("show this to a person") rather than the mechanism, so the wrong thing is harder to reach
    /// for by accident.
    /// </remarks>
    public static DateTimeOffset ToDisplayTime(this TimeProvider timeProvider, DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, timeProvider.LocalTimeZone);
}
