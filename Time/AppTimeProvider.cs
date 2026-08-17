
namespace _10xnotes.Time;

/// <summary>
/// The application's clock, fixed to the timezone its users actually live in.
/// </summary>
/// <remarks>
/// Extends the BCL <see cref="TimeProvider"/> rather than introducing a parallel clock
/// interface: it is the framework's own abstraction, so it covers both "what time is it"
/// (for code that will need to stamp a value itself) and the display conversion below,
/// and it is substitutable in tests by construction.
/// <para>
/// Only <see cref="LocalTimeZone"/> is overridden. "Local" on a server means the container's
/// clock — UTC in production — which is exactly the trap this exists to close: rendering a
/// stored instant with <c>ToLocalTime()</c> silently showed Polish users a time one or two
/// hours off their own, depending on daylight saving.
/// </para>
/// </remarks>
public sealed class AppTimeProvider : TimeProvider
{
    /// <summary>The product is Polish-language and single-audience, so one zone is enough.</summary>
    public const string DefaultTimeZoneId = "Europe/Warsaw";

    private readonly TimeZoneInfo _displayTimeZone;

    public AppTimeProvider(string? timeZoneId = null) =>
        _displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId);

    public override TimeZoneInfo LocalTimeZone => _displayTimeZone;
}
