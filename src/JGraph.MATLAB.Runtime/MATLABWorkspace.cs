using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace JGraph.MATLAB.Types;

/// <summary>
/// The MATLAB base workspace as a dictionary, as R2025b's MathWorks.MATLAB.Types.MATLABWorkspace is
/// one. It belongs to the MATLAB Engine API; a deployed component's runtime has no base workspace a
/// client reaches, in R2025b as here, so no instance is ever handed out and every member refuses.
/// </summary>
public sealed class MATLABWorkspace : IReadOnlyDictionary<string, object>
{
    private MATLABWorkspace()
    {
    }

    /// <summary>The names of the workspace's variables.</summary>
    public IEnumerable<string> Keys => throw NoWorkspace();

    /// <summary>The values of the workspace's variables.</summary>
    public IEnumerable<object> Values => throw NoWorkspace();

    /// <summary>The number of variables.</summary>
    public int Count => throw NoWorkspace();

    /// <summary>A variable by name.</summary>
    public object this[string key]
    {
        get => throw NoWorkspace();
        set => throw NoWorkspace();
    }

    /// <summary>Whether the workspace holds the variable.</summary>
    public bool ContainsKey(string key) => throw NoWorkspace();

    /// <summary>The variable's value, if the workspace holds it.</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object value) => throw NoWorkspace();

    /// <summary>The variable's value as <typeparamref name="T"/>, if the workspace holds it.</summary>
    public bool TryGetValue<T>(string key, out T value) => throw NoWorkspace();

    /// <summary>The variables.</summary>
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => throw NoWorkspace();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static InvalidOperationException NoWorkspace() =>
        new("A deployed component's runtime has no base workspace; the workspace belongs to the MATLAB Engine API.");
}
