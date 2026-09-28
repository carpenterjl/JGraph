using System.Reflection;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs.Native;
using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs.Completion;

/// <summary>
/// The names the editor offers after a dotted .NET name and inside <c>calllib('…'</c> (interop plan,
/// stage 10, ADR 0183). <see cref="Framework"/> answers for an editor with no session: the .NET
/// framework's namespaces and types, and no library. A console session answers with its own
/// assemblies and loaded libraries too.
/// </summary>
public static class InteropCompletion
{
    /// <summary>The .NET framework's names, with no session behind them.</summary>
    public static IScriptCompletionSource Framework { get; } = new Source(catalog: null, native: null);

    /// <summary>A session's names: its catalog's types and its native host's libraries.</summary>
    internal static IScriptCompletionSource For(NetCatalog catalog, Func<NativeSession?> native) => new Source(catalog, native);

    private sealed class Source(NetCatalog? catalog, Func<NativeSession?>? native) : IScriptCompletionSource
    {
        public IReadOnlyList<CompletionItem> Members(string qualifier)
        {
            // Indexing the framework reads every assembly of it, which the UI thread must not wait
            // for: the first dot starts it and offers nothing; a dot after it is built offers the names.
            if (!NetCatalog.FrameworkReady)
            {
                NetCatalog.WarmFramework();
                return [];
            }

            try
            {
                var items = new List<CompletionItem>();
                IEnumerable<(string Name, bool IsNamespace)> children = catalog is null
                    ? NetCatalog.FrameworkChildren(qualifier)
                    : catalog.Children(qualifier);
                foreach ((string name, bool isNamespace) in children)
                {
                    items.Add(isNamespace
                        ? new CompletionItem(name, CompletionItemKind.Namespace, Description: $"namespace {qualifier}.{name}")
                        : new CompletionItem(name, CompletionItemKind.Type, Description: $"class {qualifier}.{name}"));
                }

                Type? type = catalog is null ? NetCatalog.FrameworkType(qualifier) : catalog.TypeNamed(qualifier);
                if (type is not null)
                {
                    items.AddRange(StaticMembers(type));
                }

                return items;
            }
            catch (Exception fault) when (fault is InvalidOperationException or TypeLoadException or FileNotFoundException or NotSupportedException)
            {
                return []; // the session changed the catalog under us, or a type would not load
            }
        }

        public IReadOnlyList<string> Libraries()
        {
            if (!OperatingSystem.IsWindows() || native?.Invoke() is not { } session)
            {
                return [];
            }

            try
            {
                return [.. session.Libraries.Keys.Order(StringComparer.Ordinal)];
            }
            catch (InvalidOperationException)
            {
                return [];
            }
        }

        public IReadOnlyList<CompletionItem> LibraryFunctions(string library)
        {
            if (!OperatingSystem.IsWindows() || native?.Invoke()?.Library(library) is not { } loaded)
            {
                return [];
            }

            try
            {
                var items = new List<CompletionItem>();
                LibraryModel model = loaded.Model;
                foreach (KeyValuePair<string, LibFunction> function in loaded.Functions.OrderBy(static f => f.Key, StringComparer.Ordinal))
                {
                    items.Add(new CompletionItem(function.Key, CompletionItemKind.Library, model.Signature(function.Value), $"function of {library}"));
                }

                return items;
            }
            catch (InvalidOperationException)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// A type's public static members by name — methods, properties and fields, an enum's members
    /// among the fields — without accessors, operators or other special names.
    /// </summary>
    private static IEnumerable<CompletionItem> StaticMembers(Type type)
    {
        const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MemberInfo member in type.GetMembers(Statics))
        {
            bool offered = member switch
            {
                MethodInfo method => !method.IsSpecialName && !method.IsGenericMethodDefinition,
                PropertyInfo or FieldInfo => true,
                _ => false,
            };
            if (offered && seen.Add(member.Name))
            {
                string description = type.IsEnum && member is FieldInfo
                    ? $"member of {type.FullName}"
                    : $"static {(member is MethodInfo ? "method" : member is PropertyInfo ? "property" : "field")} of {type.FullName}";
                yield return new CompletionItem(member.Name, CompletionItemKind.Member, Description: description);
            }
        }
    }
}
