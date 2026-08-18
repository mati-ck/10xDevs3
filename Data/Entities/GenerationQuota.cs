namespace _10xnotes.Data.Entities;

/// <summary>
/// How many notes one user generated on one day — the ledger that keeps a runaway loop, or a
/// long evening, from burning the provider quota.
/// </summary>
/// <remarks>
/// Persisted rather than held in memory on purpose: deploys are frequent (auto-deploy on merge),
/// and an in-process counter resets on every one of them, which makes the cap advisory exactly
/// when it matters.
/// <para>
/// One row per (owner, day). <see cref="UsageDate"/> is a calendar date in the *display* timezone
/// rather than UTC — a limit that resets in the middle of the user's evening reads as broken.
/// </para>
/// </remarks>
public sealed class GenerationQuota : IOwnedByUser
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>auth.users.id</c>; unique only in combination with <see cref="UsageDate"/>.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The calendar day this row counts, in the display timezone.</summary>
    public DateOnly UsageDate { get; set; }

    /// <summary>Generations started on <see cref="UsageDate"/>.</summary>
    /// <remarks>
    /// Counts generations *started*, not finished: the slot is taken before the provider is
    /// called, so a cancelled or failed attempt still counts. That is the deliberate direction to
    /// err in — the alternative lets a series of expensive timeouts cost money without ever
    /// touching the ledger.
    /// </remarks>
    public int Count { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
