using System.Security.Cryptography;
using System.Text;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>AES-GCM encrypted credential vault. Secrets never leave through
/// CredentialView, are never logged, and are not part of workflow definitions.
/// The key comes from the host environment; the development fallback is
/// explicitly non-production and must be replaced by a host secret in deploy.</summary>
public sealed class CredentialVault : ICredentialVault
{
    private const string KeyEnvironment = "GOCORDIS_INDUSTRIAL_KEY";
    private static readonly byte[] DevelopmentKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("gocordis-industrial-development-key-v1"));
    private readonly Func<IndustrialDbContext> _db;
    private readonly byte[] _key;

    public CredentialVault(Func<IndustrialDbContext> db, bool allowDevelopmentKey = false)
        : this(db, ResolveKey(allowDevelopmentKey)) { }

    public CredentialVault(Func<IndustrialDbContext> db, byte[] key)
    {
        _db = db;
        _key = key.Length == 32 ? key : throw new ArgumentException("credential key must be 32 bytes", nameof(key));
    }

    private static byte[] ResolveKey(bool allowDevelopmentKey)
    {
        var configured = Environment.GetEnvironmentVariable(KeyEnvironment);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        }

        if (!allowDevelopmentKey)
        {
            throw new InvalidOperationException(
                "GOCORDIS_INDUSTRIAL_KEY must be set for credential storage; " +
                "development fallback is opt-in only");
        }

        return DevelopmentKey;
    }

    public async ValueTask<CredentialView> PutAsync(
        string id, string name, string kind, string secret, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var now = DateTimeOffset.UtcNow;
        var (ciphertext, nonce, tag) = Encrypt(secret);

        await using var db = _db();
        var entity = await db.Credentials.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            entity = new CredentialEntity { Id = id };
            db.Credentials.Add(entity);
        }

        entity.Name = name;
        entity.Kind = kind;
        entity.Ciphertext = ciphertext;
        entity.Nonce = nonce;
        entity.Tag = tag;
        entity.Version++;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return entity.ToView();
    }

    public async ValueTask<string> RevealAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Credentials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw new InvalidOperationException($"credential '{id}' not found");
        return Decrypt(entity.Ciphertext, entity.Nonce, entity.Tag);
    }

    public async ValueTask<CredentialView?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Credentials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity?.ToView();
    }

    public async ValueTask<IReadOnlyList<CredentialView>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Credentials.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        return rows.Select(x => x.ToView()).ToArray();
    }

    public async ValueTask<CredentialView> RotateAsync(string id, string secret, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct)
                       ?? throw new InvalidOperationException($"credential '{id}' not found");
        return await PutAsync(id, existing.Name, existing.Kind, secret, ct);
    }

    private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(string secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        CryptographicOperations.ZeroMemory(plaintext);
        return (ciphertext, nonce, tag);
    }

    private string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        var text = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return text;
    }
}

internal static class CredentialMapping
{
    public static CredentialView ToView(this CredentialEntity entity) =>
        new(entity.Id, entity.Name, entity.Kind, entity.Version, entity.UpdatedAt);
}
