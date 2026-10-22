using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

// Dump the public API of each assembly path given on the command line.
var dirs = args.Select(a => Path.GetDirectoryName(Path.GetFullPath(a))!).Distinct().ToList();
dirs.Add(@"C:\Program Files\MATLAB\R2025b\toolbox\dotnetbuilder\bin\win64\netstandard2.0");
dirs.Add(@"C:\Program Files\MATLAB\R2025b\extern\dotnet\netstandard2.0");
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    foreach (var d in dirs)
    {
        var p = Path.Combine(d, name.Name + ".dll");
        if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
    }
    return null;
};
foreach (var path in args)
{
    Console.WriteLine("==== " + path);
    Assembly asm;
    try { asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)); }
    catch (Exception e) { Console.WriteLine("load failed: " + e.Message); continue; }
    Console.WriteLine("FullName: " + asm.FullName);
    var tfa = asm.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
    Console.WriteLine("TargetFramework: " + (tfa?.FrameworkName ?? "<none>") + "  ImageRuntimeVersion: " + asm.ImageRuntimeVersion);
    foreach (var r in asm.GetReferencedAssemblies()) Console.WriteLine("  ref " + r.FullName);
    foreach (var res in asm.GetManifestResourceNames())
    {
        using var s = asm.GetManifestResourceStream(res);
        Console.WriteLine("  resource " + res + " length=" + (s?.Length ?? -1));
    }
    Type[] types;
    try { types = asm.GetExportedTypes(); }
    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
    catch (Exception e) { Console.WriteLine("types failed: " + e.Message); continue; }
    bool big = types.Length > 40;
    foreach (var t in types.OrderBy(t => t.FullName))
    {
        Console.WriteLine("  type " + t.FullName + (t.BaseType != null ? " : " + t.BaseType.Name : ""));
        if (big && !(t.Name.StartsWith("MATLAB") || t.Name == "RunOptions")) continue;
        try
        {
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                string sig = m switch
                {
                    MethodInfo mi when !mi.IsSpecialName => (mi.IsStatic ? "static " : "") + mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")",
                    ConstructorInfo ci => "ctor(" + string.Join(", ", ci.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")",
                    PropertyInfo pi => "prop " + pi.PropertyType.Name + " " + pi.Name,
                    _ => null
                } ?? "";
                if (sig.Length > 0) Console.WriteLine("      " + sig);
            }
        }
        catch (Exception e) { Console.WriteLine("      members failed: " + e.Message); }
    }
}
