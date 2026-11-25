using Gocordis.Extensions;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Adapter exposing one broker route as a provider descriptor.</summary>
public sealed class RecordReaderProvider(string fileExtension, IRecordReader reader) : IRecordReaderProvider
{
    public string FileExtension { get; } = fileExtension;
    public IRecordReader Reader { get; } = reader;
}

/// <summary>The stable IRecordReader capability backed by the shared reader
/// broker. A newly installed Reader plugin only registers one more route; no
/// workflow core, coordinator or designer code changes.</summary>
public sealed class BrokerRecordReader(Broker<IRecordReaderProvider> broker) : IRecordReader
{
    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var extension = Path.GetExtension(file.FullPath);
        var provider = broker.Providers.FirstOrDefault(candidate =>
            string.Equals(candidate.FileExtension, extension, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"no Reader plugin is registered for '{extension}' ({file.FullPath})");
        await foreach (var record in provider.Reader.ReadAsync(file, ct).WithCancellation(ct))
        {
            yield return record;
        }
    }
}
