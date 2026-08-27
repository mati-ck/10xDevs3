using System.Reflection;
using _10xnotes.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace _10xnotes.Data;

/// <summary>
/// The application's single EF Core entry point.
/// <para>
/// Owner scoping is the default here, not an opt-in: every entity implementing
/// <see cref="IOwnedByUser"/> gets a global query filter, so forgetting a <c>.Where()</c>
/// cannot leak another user's rows.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    private static readonly MethodInfo ApplyOwnerFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyOwnerFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// The user every owner-scoped query is filtered by. <see cref="Guid.Empty"/> when nobody
    /// is authenticated, which makes those queries return nothing — fail-closed by construction.
    /// <para>
    /// Settable rather than constructor-captured: contexts are created per operation by
    /// <see cref="UserScopedDbContextFactory"/>, which assigns the *current* user. EF Core
    /// re-reads this property for every query rather than baking it into the cached model,
    /// so a login or logout mid-circuit is reflected immediately.
    /// </para>
    /// </summary>
    public Guid CurrentUserId { get; set; }

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<SourceMaterial> SourceMaterials => Set<SourceMaterial>();

    public DbSet<GenerationQuota> GenerationQuotas => Set<GenerationQuota>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteEvent> NoteEvents => Set<NoteEvent>();

    /// <summary>Key ring for ASP.NET DataProtection — see <see cref="IDataProtectionKeyContext"/>.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(p => p.DisplayName).HasMaxLength(200);
            entity.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(p => p.OwnerId).IsUnique();
        });

        modelBuilder.Entity<SourceMaterial>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(m => m.Title).IsRequired().HasMaxLength(200);
            entity.Property(m => m.Content).IsRequired();

            // Text rather than an int, for the same reason NoteEvent.Kind is — see the comment
            // there. Required: every row states its provenance, including the ones that predate
            // the column and were backfilled to 'MarkdownFile'.
            entity.Property(m => m.Kind)
                .IsRequired()
                .HasMaxLength(20)
                .HasConversion<string>();

            // Optional, unlike Title: a pasted material has no file behind it, so there is no
            // name to store. Kind is what says which case a row is in — do not infer it from
            // this being null.
            entity.Property(m => m.OriginalFileName).HasMaxLength(260);
            entity.Property(m => m.CreatedAt).HasDefaultValueSql("now()");

            // Not unique, unlike the one on Profile: a user owns many materials. It exists
            // because every read path filters by owner — the global query filter puts owner_id
            // in the WHERE clause of literally every query against this table.
            entity.HasIndex(m => m.OwnerId);
        });

        modelBuilder.Entity<GenerationQuota>(entity =>
        {
            entity.HasKey(q => q.Id);

            // No gen_random_uuid()/now() defaults here, unlike Profile and SourceMaterial. Those
            // two are only ever inserted by a page that supplies neither, so a database default is
            // the right home. This row is written by GenerationQuotaService, which already holds
            // the clock that decides UsageDate — letting the database stamp CreatedAt from its own
            // UTC clock would put two timestamps that disagree about "now" on the same row. Keeping
            // both app-side also makes the ledger insertable on any provider, which is what lets
            // the limit be tested without a Postgres instance.

            // Unique, unlike the index on SourceMaterial: a second row for the same user and day
            // would silently double that user's allowance. Two tabs starting their first
            // generation of the day at once is enough to produce one, so the constraint is what
            // makes the cap real — GenerationQuotaService is written to expect the conflict.
            entity.HasIndex(q => new { q.OwnerId, q.UsageDate }).IsUnique();
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(n => n.Title).IsRequired().HasMaxLength(200);
            entity.Property(n => n.Content).IsRequired();
            entity.Property(n => n.DraftContent).IsRequired();
            entity.Property(n => n.PromptVersion).IsRequired().HasMaxLength(50);
            entity.Property(n => n.Model).IsRequired().HasMaxLength(200);
            entity.Property(n => n.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(n => n.UpdatedAt).HasDefaultValueSql("now()");

            // What makes the note-per-material relationship 1:1 rather than merely intended.
            // Without it, two tabs accepting at once leave two notes for one material and the
            // page silently shows whichever comes back first.
            entity.HasIndex(n => n.SourceMaterialId).IsUnique();

            // NO ACTION is a decision, not a default — do NOT "fix" this to Cascade.
            //
            // EF's default for a required foreign key is Cascade, which would quietly answer the
            // PRD's open question about what deleting a source material does to its note. That
            // question blocks S-06 and belongs to whoever plans it, not to a framework default.
            //
            // Restrict is not the alternative either: Postgres checks it immediately, and
            // deleting an account cascades from auth.users into source_materials and notes within
            // one statement — Restrict would fire mid-statement and make account deletion fail.
            // NO ACTION defers the check to the end of the statement, so that cascade succeeds
            // while a bare DELETE FROM source_materials still bounces.
            //
            // No navigation properties, because the project has none anywhere: ownership and
            // relationships are expressed by id, and every read is a deliberate query.
            entity.HasOne<SourceMaterial>()
                .WithMany()
                .HasForeignKey(n => n.SourceMaterialId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<NoteEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.PromptVersion).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Model).IsRequired().HasMaxLength(200);
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("now()");

            // Stored as text rather than as an int. The only consumer is a SQL query somebody
            // writes by hand against the database — "where kind = 'Saved'" is legible there,
            // "where kind = 1" needs this file open beside it.
            entity.Property(e => e.Kind)
                .IsRequired()
                .HasMaxLength(20)
                .HasConversion<string>();

            // The shape of the only query this table exists for: one user's events over a period.
            entity.HasIndex(e => new { e.OwnerId, e.OccurredAt });

            // No foreign key on SourceMaterialId, deliberately — see NoteEvent's remarks. The
            // ledger has to outlive the material, or the acceptance measurement deletes itself.
        });

        // Apply the owner filter to every entity that opts in via IOwnedByUser. Materialized
        // first because HasQueryFilter mutates the model we are iterating.
        var ownedEntityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(IOwnedByUser).IsAssignableFrom(entityType.ClrType))
            .ToList();

        foreach (var entityType in ownedEntityTypes)
        {
            ApplyOwnerFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    /// <summary>
    /// Referencing the context property <see cref="CurrentUserId"/> (rather than a captured
    /// local) is what lets EF Core turn it into a per-query parameter, so the cached model
    /// stays valid across contexts belonging to different users.
    /// </summary>
    private void ApplyOwnerFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOwnedByUser
    {
        var entity = modelBuilder.Entity<TEntity>();

        entity.HasQueryFilter(e => e.OwnerId == CurrentUserId);

        // Ownership is assigned once, at insert, and never moves. EF throws rather than
        // emitting an UPDATE that hands a row to a different owner.
        entity.Property(e => e.OwnerId).Metadata
            .SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // Puts owner_id into the WHERE clause of every UPDATE and DELETE. Without it EF targets
        // the row by primary key alone, so an entity attached with somebody else's key and the
        // caller's own OwnerId sails past the change-tracker guard below and writes their row.
        // Applied through the convention rather than on an entity, so every future IOwnedByUser
        // inherits it without anyone remembering to.
        entity.Property(e => e.OwnerId).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwners();

        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex) when (IsOwnershipViolation(ex))
        {
            throw OwnershipViolation(ex);
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampOwners();

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (IsOwnershipViolation(ex))
        {
            throw OwnershipViolation(ex);
        }
    }

    /// <summary>
    /// Whether a concurrency failure is really an ownership violation.
    /// </summary>
    /// <remarks>
    /// <see cref="IOwnedByUser.OwnerId"/> is a concurrency token, so an <c>UPDATE</c> or
    /// <c>DELETE</c> aimed at a row the caller does not own matches nothing and EF reports a
    /// concurrency conflict. With exactly one owner per row and ownership frozen after insert,
    /// there is no other way for an owned entity to produce one — a genuine concurrent edit
    /// cannot change <c>owner_id</c>. Anything involving a non-owned entity is left alone: that
    /// one really is concurrency.
    /// </remarks>
    private static bool IsOwnershipViolation(DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0
        && exception.Entries.All(entry => entry.Entity is IOwnedByUser);

    /// <summary>
    /// Reuses the wording <see cref="StampOwners"/> uses for the tracked case, so both guards —
    /// the change-tracker one and the database one — read as the same problem.
    /// </summary>
    private static InvalidOperationException OwnershipViolation(DbUpdateConcurrencyException exception) =>
        new(
            $"Cannot modify or delete a {exception.Entries[0].Metadata.ClrType.Name} owned by another user.",
            exception);

    /// <summary>
    /// Assigns ownership on insert so callers cannot forget to, and refuses to write an
    /// unowned row when nobody is authenticated.
    /// <para>
    /// Also guards the write side. Global query filters apply to queries only — never to the
    /// UPDATE/DELETE that <see cref="SaveChanges()"/> emits, which target a row by primary key
    /// alone. Without this check a tracked entity belonging to somebody else would be written
    /// unchallenged, so the isolation guarantee would cover reads and nothing more.
    /// </para>
    /// </summary>
    private void StampOwners()
    {
        var touched = ChangeTracker.Entries<IOwnedByUser>()
            .Where(entry => entry.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted)
            .ToList();

        if (touched.Count == 0)
        {
            return;
        }

        if (CurrentUserId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot persist a user-owned entity without an authenticated user.");
        }

        foreach (var entry in touched)
        {
            if (entry.State == EntityState.Added)
            {
                // Ownership always comes from the current user, never from the caller. A
                // supplied OwnerId is overwritten rather than trusted, so binding it from
                // user input cannot create a row owned by somebody else.
                entry.Entity.OwnerId = CurrentUserId;
                continue;
            }

            if (entry.Entity.OwnerId != CurrentUserId)
            {
                throw new InvalidOperationException(
                    $"Cannot modify or delete a {entry.Metadata.ClrType.Name} owned by another user.");
            }
        }
    }
}
