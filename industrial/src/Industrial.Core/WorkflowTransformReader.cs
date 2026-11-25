using System.Runtime.CompilerServices;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Applies the workflow's mapping/validation nodes to the record
/// stream before delivery. The workflow definition stays the single source of
/// truth; transforms are ordinary Cordis capabilities, not Elsa Studio JSON.</summary>
public sealed class WorkflowTransformReader(
    IRecordReader inner,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> mappings,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> validations) : IRecordReader
{
    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var mappingSteps = mappings.Select(config => new MappingTransform(config)).ToArray();
        var validationSteps = validations.Select(config => new ValidationTransform(config)).ToArray();
        await foreach (var original in inner.ReadAsync(file, ct).WithCancellation(ct))
        {
            var record = original;
            foreach (var step in mappingSteps)
            {
                record = step.Apply(record);
            }
            var valid = true;
            foreach (var step in validationSteps)
            {
                if (!step.TryApply(record, out var validated, out _))
                {
                    valid = false;
                    break;
                }
                record = validated!;
            }
            if (valid) yield return record;
        }
    }
}
