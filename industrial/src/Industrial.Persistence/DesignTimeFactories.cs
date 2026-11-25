using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Industrial.Persistence;

public sealed class IndustrialDbContextFactory : IDesignTimeDbContextFactory<IndustrialDbContext>
{
    public IndustrialDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite("Data Source=industrial-ledger.db")
            .Options);
}

public sealed class IndustrialTargetDbContextFactory : IDesignTimeDbContextFactory<IndustrialTargetDbContext>
{
    public IndustrialTargetDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IndustrialTargetDbContext>()
            .UseSqlite("Data Source=industrial-target.db")
            .Options);
}

public sealed class IndustrialStageDbContextFactory : IDesignTimeDbContextFactory<IndustrialStageDbContext>
{
    public IndustrialStageDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IndustrialStageDbContext>()
            .UseSqlite("Data Source=industrial-stages.db")
            .Options);
}
