namespace Gocordis.Plugins;

/// <summary>
/// A declaration file could not be interpreted as plugin manifests. Discovery
/// records this per file so one broken declaration cannot hide healthy ones.
/// </summary>
public sealed class ManifestParseException : Exception
{
    public ManifestParseException(string message)
        : base(message)
    {
    }

    public ManifestParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
