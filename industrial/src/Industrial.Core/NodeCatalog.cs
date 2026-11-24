using System.Collections.Concurrent;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Runtime catalog of node descriptions contributed by plugins.
/// Descriptors are data, not lifecycle state; the plugin owns registration and
/// withdrawal as a Cordis effect.</summary>
public sealed class NodeCatalog : INodeCatalog
{
    private readonly ConcurrentDictionary<string, NodeDescriptor> _nodes = new(StringComparer.Ordinal);

    public ValueTask RegisterAsync(NodeDescriptor descriptor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.NodeTypeId))
        {
            throw new ArgumentException("node type id is required", nameof(descriptor));
        }

        if (!_nodes.TryAdd(descriptor.NodeTypeId, descriptor))
        {
            throw new InvalidOperationException($"node type '{descriptor.NodeTypeId}' is already registered");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<NodeDescriptor?> GetAsync(string nodeTypeId, CancellationToken ct = default) =>
        ValueTask.FromResult(_nodes.TryGetValue(nodeTypeId, out var descriptor) ? descriptor : null);

    public ValueTask<IReadOnlyList<NodeDescriptor>> ListAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<NodeDescriptor>>(
            _nodes.Values.OrderBy(node => node.Category).ThenBy(node => node.NodeTypeId).ToArray());

    public ValueTask<bool> UnregisterAsync(string nodeTypeId, CancellationToken ct = default) =>
        ValueTask.FromResult(_nodes.TryRemove(nodeTypeId, out _));
}
