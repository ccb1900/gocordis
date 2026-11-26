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
    public DbSet<CollectionRunDeliveryEntity> CollectionRunDeliveries => Set<CollectionRunDeliveryEntity>();

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
            entity.HasIndex(x => x.InstanceId);
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
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAtUnixMs });
            entity.HasIndex(x => x.ClaimToken).IsUnique();
            entity.Property(x => x.Version).IsConcurrencyToken();
        });

        model.Entity<CollectionRunDeliveryEntity>(entity =>
        {
            entity.HasKey(x => new { x.RunId, x.DeliveryId });
            entity.HasIndex(x => x.DeliveryId);
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
    /// <summary>The Elsa instance that produced this run. Retry resolves the
    /// original instance through this field instead of guessing by instance id
    /// string order.</summary>
    public string? InstanceId { get; set; }
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
    public string? ClaimToken { get; set; }
    /// <summary>The run that most recently actually attempted this delivery.
    /// Retry follows this field, not the historical run association set.</summary>
    public string? LastAttemptRunId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public long LeaseExpiresAtUnixMs { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Warnings { get; set; }
    public string? StatisticsJson { get; set; }
    public long Version { get; set; }
}

public sealed class CollectionRunDeliveryEntity
{
    public string RunId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string Status { get; set; } = "Considered";
    public string? ClaimToken { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Delivery target database. Kept separate from the collection ledger
/// so business rows and processing state have different authorities.</summary>
public sealed class StageOutputEntity
{
    public string Id { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CommittedAt { get; set; }
}

public sealed class IndustrialStageDbContext : DbContext
{
    public IndustrialStageDbContext(DbContextOptions<IndustrialStageDbContext> options) : base(options) { }

    public DbSet<StageOutputEntity> StageOutputs => Set<StageOutputEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StageOutputEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InstanceId, x.NodeId }).IsUnique();
        });
    }
}

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

        var existed = File.Exists(path);
        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite($"Data Source={path}", sql => sql.MigrationsAssembly("Industrial.Persistence"))
            .Options;
        var context = new IndustrialDbContext(options);
        if (existed)
        {
            context.Database.EnsureCreated();
            UpgradeLedgerSchema(context);
        }
        else
        {
            context.Database.Migrate();
        }
        EnsureScheduleFireTable(context);
        return context;
    }

    private static void EnsureScheduleFireTable(IndustrialDbContext context)
    {
        // Read by raw SQL from CollectionLedger; kept outside EF migrations so
        // both fresh and legacy ledger files get the same lifecycle semantics.
        Execute(context, """
            CREATE TABLE IF NOT EXISTS ScheduleFires (
                Key TEXT NOT NULL PRIMARY KEY,
                WorkflowId TEXT NOT NULL,
                Version INTEGER NOT NULL,
                DueAtUtc TEXT NOT NULL,
                FiredAtUtc TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                Attempts INTEGER NOT NULL DEFAULT 0,
                ClaimToken TEXT NULL,
                LeaseExpiresAtUnixMs INTEGER NOT NULL DEFAULT 0,
                Error TEXT NULL,
                FinishedAtUtc TEXT NULL,
                PlannedAtUtc TEXT NULL);
            """);
        // Legacy ledger files created the five-column shape; bring them up to
        // the same lifecycle schema without losing already-fired rows.
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN Status TEXT NOT NULL DEFAULT 'Pending';");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN Attempts INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN ClaimToken TEXT NULL;");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN LeaseExpiresAtUnixMs INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN Error TEXT NULL;");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN FinishedAtUtc TEXT NULL;");
        Execute(context, "ALTER TABLE ScheduleFires ADD COLUMN PlannedAtUtc TEXT NULL;");
        // Rows without PlannedAtUtc were claimed by the old at-most-once
        // scheduler; mark them terminal so upgrading never re-executes a fire.
        Execute(context,
            "UPDATE ScheduleFires SET Status = 'Succeeded' WHERE PlannedAtUtc IS NULL OR PlannedAtUtc = '';");
        Execute(context,
            "CREATE INDEX IF NOT EXISTS IX_ScheduleFires_Status_Due ON ScheduleFires(Status, DueAtUtc);");
        Execute(context,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ScheduleFires_ClaimToken ON ScheduleFires(ClaimToken) WHERE ClaimToken IS NOT NULL;");
        // Per-version scan position: catch-up starts where this workflow version
        // last looked, so low-frequency cron jobs are not lost after downtime.
        Execute(context, """
            CREATE TABLE IF NOT EXISTS ScheduleCheckpoints (
                WorkflowId TEXT NOT NULL,
                Version INTEGER NOT NULL,
                CheckpointUtc TEXT NOT NULL,
                Skipped INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (WorkflowId, Version));
            """);
    }

    private static void UpgradeLedgerSchema(IndustrialDbContext context)
    {
        // EnsureCreated never alters an existing SQLite file. Keep the ledger
        // upgrade idempotent so older local databases keep working.
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN TargetKind TEXT NOT NULL DEFAULT 'record';");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN Records INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN IdempotencyKey TEXT NOT NULL DEFAULT '';");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN Version INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN ClaimedAt TEXT NULL;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN ClaimToken TEXT NULL;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN LeaseExpiresAt TEXT NULL;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN LeaseExpiresAtUnixMs INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN Accepted INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN Rejected INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN Warnings INTEGER NOT NULL DEFAULT 0;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN StatisticsJson TEXT NULL;");
        Execute(context, "ALTER TABLE CollectionRuns ADD COLUMN InstanceId TEXT NULL;");
        Execute(context, "ALTER TABLE FileDeliveries ADD COLUMN LastAttemptRunId TEXT NULL;");
        Execute(context, "CREATE INDEX IF NOT EXISTS IX_CollectionRuns_InstanceId ON CollectionRuns(InstanceId);");
        Execute(context, """
            CREATE TABLE IF NOT EXISTS CollectionRunDeliveries (
                RunId TEXT NOT NULL,
                DeliveryId TEXT NOT NULL,
                Status TEXT NOT NULL,
                ClaimToken TEXT NULL,
                LastError TEXT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (RunId, DeliveryId));
            """);
        Execute(context,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_FileDeliveries_ClaimToken ON FileDeliveries(ClaimToken) WHERE ClaimToken IS NOT NULL;");
        Execute(context,
            "CREATE INDEX IF NOT EXISTS IX_FileDeliveries_Status_Lease ON FileDeliveries(Status, LeaseExpiresAtUnixMs);");
        Execute(context,
            "CREATE INDEX IF NOT EXISTS IX_CollectionRunDeliveries_DeliveryId ON CollectionRunDeliveries(DeliveryId);");
    }

    private static void Execute(IndustrialDbContext context, string sql)
    {
        try
        {
            context.Database.ExecuteSqlRaw(sql);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (
            ex.SqliteErrorCode == 1 &&
            (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
        {
            // Idempotent upgrade: column/index already present.
        }
    }

    public static IndustrialStageDbContext CreateStages(string root)
    {
        var path = Path.GetFullPath(Path.Combine(root, "industrial-stages.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new DbContextOptionsBuilder<IndustrialStageDbContext>()
            .UseSqlite($"Data Source={path}", sql => sql.MigrationsAssembly("Industrial.Persistence"))
            .Options;
        var context = new IndustrialStageDbContext(options);
        context.Database.Migrate();
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
            .UseSqlite($"Data Source={path}", sql => sql.MigrationsAssembly("Industrial.Persistence"))
            .Options;
        var context = new IndustrialTargetDbContext(options);
        context.Database.Migrate();
        return context;
    }
}
