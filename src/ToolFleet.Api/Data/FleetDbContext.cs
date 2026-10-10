using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ToolFleet.Api.Infrastructure;
using ToolFleet.Domain;
using ToolFleet.Domain.Rollouts;

namespace ToolFleet.Api.Data;

public class FleetDbContext(
    DbContextOptions<FleetDbContext> options,
    ICurrentUser currentUser,
    TimeProvider clock) : DbContext(options)
{
    private static readonly JsonSerializerOptions AuditJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DbSet<FieldLocation> Locations => Set<FieldLocation>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<FirmwareRelease> FirmwareReleases => Set<FirmwareRelease>();
    public DbSet<Rollout> Rollouts => Set<Rollout>();
    public DbSet<RolloutTarget> RolloutTargets => Set<RolloutTarget>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server forgets DateTime.Kind; every DateTime in this app is UTC, so say so on the way out.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldLocation>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(60);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Tool>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SerialNumber).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.FirmwareVersion).HasMaxLength(20);
            e.HasIndex(x => x.SerialNumber).IsUnique();
            e.HasIndex(x => x.Model);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FirmwareRelease>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.Version).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.Model, x.Version }).IsUnique();
        });

        modelBuilder.Entity<Rollout>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.TargetVersion).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HaltReason).HasMaxLength(300);
            e.HasOne<FirmwareRelease>().WithMany().HasForeignKey(x => x.FirmwareReleaseId).OnDelete(DeleteBehavior.Restrict);

            // Targets is a read-only list backed by the private _targets field.
            e.HasMany(x => x.Targets).WithOne().HasForeignKey(t => t.RolloutId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RolloutTarget>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.PreviousVersion).HasMaxLength(20);
            e.Property(x => x.FailureReason).HasMaxLength(300);
            e.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RolloutId, x.ToolId }).IsUnique();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(120);
            e.Property(x => x.EntityType).HasMaxLength(60);
            e.Property(x => x.EntityId).HasMaxLength(60);
            e.Property(x => x.Action).HasMaxLength(10);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });
    }

    /// <summary>Adds audit rows for every change, so they commit (or fail) in the same transaction.</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddAuditEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void AddAuditEntries()
    {
        ChangeTracker.DetectChanges();

        var now = clock.GetUtcNow().UtcDateTime;
        var actor = currentUser.Name;

        var changed = ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in changed)
        {
            AuditEntries.Add(new AuditEntry
            {
                AtUtc = now,
                Actor = actor,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = KeyOf(entry),
                Action = entry.State switch
                {
                    EntityState.Added => "Insert",
                    EntityState.Modified => "Update",
                    _ => "Delete",
                },
                Changes = JsonSerializer.Serialize(ChangesOf(entry), AuditJson),
            });
        }
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue));

    private static Dictionary<string, object?> ChangesOf(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue),
        EntityState.Modified => entry.Properties
            .Where(p => p.IsModified)
            .ToDictionary(p => p.Metadata.Name, p => (object?)new { from = p.OriginalValue, to = p.CurrentValue }),
        _ => new Dictionary<string, object?>(),
    };
}

/// <summary>Marks DateTimes read from the database as UTC.</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
