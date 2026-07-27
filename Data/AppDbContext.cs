using System.Reflection;
using _10xnotes.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.OwnerId == CurrentUserId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwners();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampOwners();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Assigns ownership on insert so callers cannot forget to, and refuses to write an
    /// unowned row when nobody is authenticated.
    /// </summary>
    private void StampOwners()
    {
        var added = ChangeTracker.Entries<IOwnedByUser>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        if (added.Count == 0)
        {
            return;
        }

        if (CurrentUserId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot persist a user-owned entity without an authenticated user.");
        }

        foreach (var entry in added.Where(entry => entry.Entity.OwnerId == Guid.Empty))
        {
            entry.Entity.OwnerId = CurrentUserId;
        }
    }
}
