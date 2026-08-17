namespace _10xnotes.Generation;

/// <summary>Configuration for the note-generation provider.</summary>
/// <remarks>
/// Endpoint and model are plain configuration on purpose: the whole point of going through
/// <c>IChatClient</c> is that swapping model or provider is an environment change, not a code
/// change. <see cref="ApiKey"/> is the one value that must never live in
/// <c>appsettings.json</c> — unlike <c>Supabase:AnonKey</c>, which is publishable by design,
/// this key spends money. It follows <c>ConnectionStrings__Postgres</c>: user-secrets locally,
/// <c>Ai__ApiKey</c> as an environment variable in production.
/// </remarks>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>OpenAI-compatible base address. OpenRouter by default.</summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Provider-qualified model id. The default is a fast, long-context model priced low enough
    /// that a full 128 KB material costs on the order of a cent to summarise.
    /// </summary>
    public string Model { get; set; } = "google/gemini-3.7-flash";

    /// <summary>Secret. Empty means "not configured", which fails closed rather than at startup.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Upper bound on a single generation. Generous because a long material on a slow provider
    /// legitimately takes a minute; it exists to stop a hung stream, not to police latency.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Generations allowed per user per day. Read by the quota ledger, kept here so every
    /// generation knob is in one section.
    /// </summary>
    public int DailyGenerationLimit { get; set; } = 50;
}
