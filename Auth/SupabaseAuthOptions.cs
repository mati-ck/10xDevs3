namespace _10xnotes.Auth;

/// <summary>Configuration for the Supabase Auth (GoTrue) endpoint.</summary>
public sealed class SupabaseAuthOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Project URL, e.g. <c>https://&lt;ref&gt;.supabase.co</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The project's anon key. Publishable by design — it already ships in client apps — so
    /// unlike the connection string it may live in configuration.
    /// </summary>
    public string AnonKey { get; set; } = string.Empty;
}
