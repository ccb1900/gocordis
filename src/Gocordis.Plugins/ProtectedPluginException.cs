namespace Gocordis.Plugins;

/// <summary>
/// A platform-owned declaration cannot be disabled or removed by a catalog
/// action. Mirrors Go's protected-component lifecycle boundary.
/// </summary>
public sealed class ProtectedPluginException : InvalidOperationException
{
    public ProtectedPluginException(string pluginId)
        : base($"plugin '{pluginId}' is protected by the host and cannot be disabled")
    {
        PluginId = pluginId;
    }

    public string PluginId { get; }
}
