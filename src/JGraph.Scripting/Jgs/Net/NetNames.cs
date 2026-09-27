namespace JGraph.Scripting.Jgs.Net;

/// <summary>How MATLAB spells a .NET type (measured in R2025b, interop plan step 0).</summary>
internal static class NetNames
{
    /// <summary>
    /// The class name MATLAB reports: the full name, generic arguments in angle brackets with the dots
    /// of each argument's name written as <c>*</c> (<c>System.Nullable&lt;System*Int32&gt;</c>), and an
    /// array as its element's name with the rank's brackets (<c>System.Double[]</c>).
    /// </summary>
    public static string ClassName(Type type)
    {
        if (type.IsArray)
        {
            return ClassName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            Type definition = type.GetGenericTypeDefinition();
            string name = definition.FullName ?? definition.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return name + "<" + string.Join(",", type.GetGenericArguments().Select(a => ClassName(a).Replace('.', '*'))) + ">";
        }

        // A nested type keeps .NET's '+' (JGTest.Outer+Inner, measured in net_members).
        return type.FullName ?? type.Name;
    }

    /// <summary>The short name a display leads with: the class name after its namespace.</summary>
    public static string ShortName(Type type)
    {
        string full = ClassName(type);
        int bracket = full.IndexOf('<', StringComparison.Ordinal);
        int dot = full.LastIndexOf('.', bracket < 0 ? full.Length - 1 : bracket);
        return dot < 0 ? full : full[(dot + 1)..];
    }
}
