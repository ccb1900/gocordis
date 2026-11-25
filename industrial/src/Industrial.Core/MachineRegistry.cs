using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Machines are business assets with their own identity; they are not
/// resources (delivery targets) and never carry secret material.</summary>
public sealed class MachineRegistry : IMachineRegistry
{
    private readonly Func<IndustrialDbContext> _db;

    public MachineRegistry(Func<IndustrialDbContext> db) => _db = db;

    public async ValueTask<MachineDefinition> RegisterAsync(
        MachineDefinition machine, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        if (string.IsNullOrWhiteSpace(machine.Id) || string.IsNullOrWhiteSpace(machine.Name))
        {
            throw new ArgumentException("machine id and name are required", nameof(machine));
        }

        await using var db = _db();
        var existing = await db.Machines.SingleOrDefaultAsync(x => x.Id == machine.Id, ct);
        var entity = existing ?? new MachineEntity { Id = machine.Id, CreatedAt = machine.CreatedAt };
        entity.Name = machine.Name;
        entity.Endpoint = machine.Endpoint;
        entity.CredentialId = machine.CredentialId;
        entity.Enabled = machine.Enabled;
        entity.UpdatedAt = machine.UpdatedAt;
        if (entity.CreatedAt == default) entity.CreatedAt = machine.CreatedAt;
        if (existing is null) db.Machines.Add(entity);
        await db.SaveChangesAsync(ct);
        return new MachineDefinition(entity.Id, entity.Name, entity.Endpoint, entity.CredentialId,
            entity.Enabled, entity.CreatedAt, entity.UpdatedAt);
    }

    public async ValueTask<MachineDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Machines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity is null
            ? null
            : new MachineDefinition(entity.Id, entity.Name, entity.Endpoint, entity.CredentialId,
                entity.Enabled, entity.CreatedAt, entity.UpdatedAt);
    }

    public async ValueTask<IReadOnlyList<MachineDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Machines.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        return rows.Select(entity => new MachineDefinition(entity.Id, entity.Name, entity.Endpoint,
            entity.CredentialId, entity.Enabled, entity.CreatedAt, entity.UpdatedAt)).ToArray();
    }

    public async ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Machines.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;
        db.Machines.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
