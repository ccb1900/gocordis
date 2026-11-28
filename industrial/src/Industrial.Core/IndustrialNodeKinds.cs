namespace Industrial.Core;

/// <summary>Provider-type classification shared by discovery/session code.
/// Source identity comes from the node's execution contract, not from graph
/// topology, so adding or reconnecting nodes cannot silently change which
/// resources need a credentialed session.</summary>
public static class IndustrialNodeKinds
{
    public static bool IsSource(string nodeType) =>
        nodeType.Contains("source", StringComparison.OrdinalIgnoreCase) ||
        nodeType.Contains("filesystem", StringComparison.OrdinalIgnoreCase);

    public static bool IsSink(string nodeType) =>
        nodeType.Contains("sink", StringComparison.OrdinalIgnoreCase);

    public static bool IsArchive(string nodeType) =>
        nodeType.Contains("archive", StringComparison.OrdinalIgnoreCase);
}
