using Microsoft.EntityFrameworkCore;

namespace Industrial.Persistence;

/// <summary>Business data authority. Plugin installation/runtime state stays in
/// Cordis; this context owns only industrial resources, credentials, workflow
/// definitions and collection ledger rows.</summary>
public sealed class IndustrialDbContext : DbContext
{
    public IndustrialDbContext(DbContextOptions<IndustrialDbContext> options) : base(options) { }

    public DbSet<ResourceEntity> Resources => Set<ResourceEntity>();
    public DbSet<MachineEntity> Machines => Set<MachineEntity>();
    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();
    public DbSet<WorkflowEntity> Workflows => Set<WorkflowEntity>();
    public DbSet<CollectionRunEntity> CollectionRuns => Set<CollectionRunEntity>();
    public DbSet<CollectionFileEntity> CollectionFiles => Set<CollectionFileEntity>();
    public DbSet<FileDeliveryEntity> FileDeliveries => Set<FileDeliveryEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ResourceEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.Type);
        });

        model.Entity<MachineEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Enabled);
        });

        model.Entity<CredentialEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });

        model.Entity<WorkflowEntity>(entity =>
        {
            entity.HasKey(x => new { x.WorkflowId, x.Version, x.Kind });
            entity.HasIndex(x => x.WorkflowId).IsUnique().HasFilter("\"IsPublished\" = 1");
        });

        model.Entity<CollectionRunEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.WorkflowId);
        });

        model.Entity<CollectionRunEntity>(entity =>
        {
            entity.Property(x => x.Version).IsConcurrencyToken();
        });

        model.Entity<CollectionFileEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ResourceId, x.IdentityKey }).IsUnique();
            entity.HasIndex(x => x.CollectionId);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });

        model.Entity<FileDeliveryEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CollectionFileId, x.TargetId }).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
    }
}

public sealed class ResourceEntity
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = string.Empty;
    public string? CredentialId { get; set; }
    public long Version { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MachineEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string? CredentialId { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CredentialEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WorkflowEntity
{
    public string WorkflowId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Kind { get; set; } = "Draft";
    public string DefinitionJson { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public string? ElsaDefinitionJson { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class CollectionRunEntity
{
    public string Id { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long Version { get; set; }
}

public sealed class CollectionFileEntity
{
    public string Id { get; set; } = string.Empty;
    public string CollectionId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Length { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset SourceDate { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Version { get; set; }
}

public sealed class FileDeliveryEntity
{
    public string Id { get; set; } = string.Empty;
    public string CollectionFileId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetKind { get; set; } = "record";
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Records { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long Version { get; set; }
}

/// <summary>Delivery target database. Kept separate from the collection ledger
/// so business rows and processing state have different authorities.</summary>
public sealed class IndustrialTargetDbContext : DbContext
{
    public IndustrialTargetDbContext(DbContextOptions<IndustrialTargetDbContext> options) : base(options) { }

    public DbSet<TargetRecordEntity> Records => Set<TargetRecordEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TargetRecordEntity>(entity =>
        {
            entity.HasKey(x => x.RecordId);
            entity.HasIndex(x => x.SourceIdentityKey);
            entity.HasIndex(x => x.RecordTime);
        });
    }
}

public sealed class TargetRecordEntity
{
    public string RecordId { get; set; } = string.Empty;
    public string SourceIdentityKey { get; set; } = string.Empty;
    public DateTimeOffset RecordTime { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public static class IndustrialStore
{
    public static IndustrialDbContext Create(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var context = new IndustrialDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static IndustrialTargetDbContext CreateTarget(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<IndustrialTargetDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var context = new IndustrialTargetDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
