using System.Text.Json;
using AppForge.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppForge.Infrastructure.Data;

public sealed class AppForgeDbContext(DbContextOptions<AppForgeDbContext> options) : DbContext(options)
{
    public DbSet<SoftwareProfileEntity> Profiles => Set<SoftwareProfileEntity>();

    public DbSet<ProfileRevisionEntity> ProfileRevisions => Set<ProfileRevisionEntity>();

    public DbSet<SoftwareVersionEntity> SoftwareVersions => Set<SoftwareVersionEntity>();

    public DbSet<BuildingBlockEntity> BuildingBlocks => Set<BuildingBlockEntity>();

    public DbSet<BuildEntity> Builds => Set<BuildEntity>();

    public DbSet<ArtifactEntity> Artifacts => Set<ArtifactEntity>();

    public DbSet<TestRunEntity> TestRuns => Set<TestRunEntity>();

    public DbSet<SigningCertificateEntity> Certificates => Set<SigningCertificateEntity>();

    public DbSet<SigningJobEntity> SigningJobs => Set<SigningJobEntity>();

    public DbSet<ApprovalRequestEntity> ApprovalRequests => Set<ApprovalRequestEntity>();

    public DbSet<ApprovalDecisionEntity> ApprovalDecisions => Set<ApprovalDecisionEntity>();

    public DbSet<PublishTargetEntity> PublishTargets => Set<PublishTargetEntity>();

    public DbSet<PublicationEntity> Publications => Set<PublicationEntity>();

    public DbSet<NotificationRuleEntity> NotificationRules => Set<NotificationRuleEntity>();

    public DbSet<AppControlRuleEntity> AppControlRules => Set<AppControlRuleEntity>();

    public DbSet<AppControlPolicyEntity> AppControlPolicies => Set<AppControlPolicyEntity>();

    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();

    public DbSet<JobEntity> Jobs => Set<JobEntity>();

    public DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();

    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var listConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        var listComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
            v => v.ToList());

        // DateTimeOffset ist in SQLite nicht sortierbar – als UTC-Ticks speichern (für SQL Server egal, aber einheitlich).
        var dtoConverter = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        var dtoNullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.ClrType.GetProperties())
            {
                if (property.PropertyType == typeof(DateTimeOffset))
                {
                    modelBuilder.Entity(entityType.ClrType).Property(property.Name).HasConversion(dtoConverter);
                }
                else if (property.PropertyType == typeof(DateTimeOffset?))
                {
                    modelBuilder.Entity(entityType.ClrType).Property(property.Name).HasConversion(dtoNullableConverter);
                }
                else if (property.PropertyType == typeof(List<string>))
                {
                    modelBuilder.Entity(entityType.ClrType).Property(property.Name).HasConversion(listConverter, listComparer);
                }
            }
        }

        modelBuilder.Entity<SoftwareProfileEntity>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Vendor, x.Product }).IsUnique();
            e.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => x.ProfileId);
        });
        modelBuilder.Entity<ProfileRevisionEntity>().HasIndex(x => new { x.ProfileId, x.Revision }).IsUnique();
        modelBuilder.Entity<SoftwareVersionEntity>().HasIndex(x => new { x.ProfileId, x.Version, x.Architecture }).IsUnique();
        modelBuilder.Entity<BuildingBlockEntity>().HasIndex(x => new { x.TenantId, x.Key, x.Version }).IsUnique();
        modelBuilder.Entity<BuildEntity>(e =>
        {
            e.HasMany(x => x.Artifacts).WithOne().HasForeignKey(x => x.BuildId);
            e.HasMany(x => x.TestRuns).WithOne().HasForeignKey(x => x.BuildId);
        });
        modelBuilder.Entity<SigningJobEntity>().HasMany(x => x.Artifacts).WithOne().HasForeignKey(x => x.SigningJobId);
        modelBuilder.Entity<ApprovalRequestEntity>(e =>
        {
            e.HasMany(x => x.Decisions).WithOne().HasForeignKey(x => x.RequestId);
            e.HasIndex(x => new { x.SubjectType, x.SubjectId });
        });
        modelBuilder.Entity<AuditEntryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Hash).IsUnique();
            e.HasIndex(x => x.PrevHash).IsUnique();
        });
        modelBuilder.Entity<JobEntity>(e =>
        {
            e.HasIndex(x => new { x.State, x.EnqueuedAt });
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
        });
        modelBuilder.Entity<SettingEntity>().HasKey(x => new { x.TenantId, x.Key });
    }
}

/// <summary>Schlüssel/Wert-Einstellungen (globale Standards, Einrichtungsassistent, Freigabeoptionen).</summary>
public sealed class SettingEntity
{
    public Guid TenantId { get; set; } = TenantEntity.DefaultTenant;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;
}
