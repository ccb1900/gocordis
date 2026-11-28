using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>One workflow execution owns every source resource session. Source
/// nodes are identified by the published graph (no incoming edge) and may
/// point at different resources; leases are released in reverse order when the
/// run, recovery or retry finishes.</summary>
public static class SourceSessionScope
{
    public static async ValueTask<IAsyncDisposable> AcquireAsync(
        IFileSource source, WorkflowDefinition definition, string defaultResourceId,
        INodeCatalog? catalog = null, CancellationToken ct = default)
    {
        var resourceIds = new HashSet<string>(StringComparer.Ordinal) { defaultResourceId };
        foreach (var node in definition.Nodes)
        {
            if (!await IsSourceNode(node, catalog, ct)) continue;
            if (node.Config.TryGetValue("resourceId", out var configured) &&
                configured is not null && !string.IsNullOrWhiteSpace(configured.ToString()))
                resourceIds.Add(configured.ToString()!);
        }

        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var resourceId in resourceIds)
            {
                var lease = await source.LeaseScopeAsync(resourceId, ct);
                if (lease is not null) leases.Add(lease);
            }
        }
        catch
        {
            // A later resource may fail authentication after earlier sessions
            // were established. Release them before propagating the error.
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync();
            throw;
        }
        return new SessionLeases(leases);
    }

    private static async ValueTask<bool> IsSourceNode(
        WorkflowNode node, INodeCatalog? catalog, CancellationToken ct)
    {
        if (catalog is not null)
        {
            var descriptor = await catalog.GetAsync(node.Type, ct);
            if (descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.Role))
                return string.Equals(descriptor.Role, "source", StringComparison.OrdinalIgnoreCase);
        }
        return IndustrialNodeKinds.IsSource(node.Type);
    }

    private sealed class SessionLeases(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync();
        }
    }
}
