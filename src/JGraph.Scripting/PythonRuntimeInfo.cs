namespace JGraph.Scripting;

/// <summary>
/// What <see cref="PythonLocator"/> discovered about an installed CPython runtime: the shared library
/// to load, the home prefix the embedded interpreter should use (venv-aware — <c>sys.prefix</c>, not
/// <c>base_prefix</c>, so the environment's own <c>site-packages</c> resolves), and the interpreter's
/// real module search paths. <see cref="Home"/> and <see cref="SearchPaths"/> may be empty when only
/// the DLL is known (e.g. a <c>PYTHONNET_PYDLL</c> override that probing could not corroborate).
/// </summary>
/// <param name="Dll">The full path of the CPython shared library.</param>
/// <param name="Home">The interpreter's <c>sys.prefix</c>, or null when unknown.</param>
/// <param name="SearchPaths">The interpreter's <c>sys.path</c> entries, or empty when unknown.</param>
public sealed record PythonRuntimeInfo(string Dll, string? Home, IReadOnlyList<string> SearchPaths);
