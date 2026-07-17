namespace JGraph.Plugins;

/// <summary>
/// Thrown when a plugin cannot be loaded or configured — a duplicate registration, a plugin whose
/// <see cref="IPlugin.Configure"/> throws, or an assembly that cannot be scanned. The message names
/// the offending plugin or file; the original fault is preserved as the inner exception.
/// </summary>
public sealed class PluginException : Exception
{
    public PluginException(string message)
        : base(message)
    {
    }

    public PluginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
