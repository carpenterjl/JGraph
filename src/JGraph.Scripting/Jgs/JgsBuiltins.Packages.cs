using System.IO;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The parts of <c>help</c> that read the folders (the packages and class folders plan, stage P1,
/// ADR 0307): a package's listing, and a code file's help text — its first comment block, or a
/// sentence with the function's header when it has none — as R2025b prints them (measured in the
/// fixture <c>p1_packages</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The help text for a name no catalog entry answers: a package member by its dotted name
    /// (<c>pkg.f</c>), a package or sub-package (<c>pkg</c>, <c>pkg.sub</c>), or a code file on the
    /// search path (<c>p1plain</c>). Null when the folders hold nothing of the name. A member file
    /// is asked before a sub-package of the same name, as the dotted name itself resolves.
    /// </summary>
    internal static string? UserHelp(JgsFunctionPath search, string name)
    {
        // pkg.C/m: a method of a class in a package, named with a slash (P2, measured).
        int slash = name.IndexOf('/');
        if (slash > 0 && name.IndexOf('/', slash + 1) < 0 && JgsCodeTree.DottedParts(name[..slash]) is not null
            && search.FindDotted(name[..slash], out _) is { } slashed)
        {
            return MethodHelp(slashed, name[..slash], name[(slash + 1)..]);
        }

        // Thing/extent, Thing.scale: a class folder's method file (stage P4, measured).
        if (JgsFunctionPath.SlashForm(name) is { } slashForm && search.ClassMethodFile(slashForm.Class, slashForm.Member) is { } methodFile)
        {
            return ClassFolderMethodHelp(search, methodFile, slashForm.Class, slashForm.Member);
        }

        if (JgsCodeTree.DottedParts(name) is { } parts)
        {
            if (search.FindDotted(name, out _) is { } memberFile)
            {
                return FileHelp(search, memberFile, name);
            }

            // pkg.C.m, pkg.C.stat: a method of a class in a package, named with a dot (P2, measured).
            string owner = string.Join('.', parts[..^1]);
            if (parts.Length > 2 && search.FindDotted(owner, out _) is { } classFile && KindOfFile(classFile) == "class"
                && MethodHelp(classFile, owner, parts[^1]) is { } method)
            {
                return method;
            }

            if (parts.Length == 2 && search.ClassMethodFile(parts[0], parts[1]) is { } dottedMethodFile)
            {
                return ClassFolderMethodHelp(search, dottedMethodFile, parts[0], parts[1]);
            }

            return search.Tree.IsPackage(parts) ? PackageHelp(search, parts) : null;
        }

        if (JgsCodeTree.DottedParts("a." + name) is null)
        {
            return null; // not an identifier: a path or an expression, which help has no answer for
        }

        if (search.Find(name) is { } file)
        {
            return FileHelp(search, file, name);
        }

        return search.Tree.IsPackage([name]) ? PackageHelp(search, [name]) : null;
    }

    /// <summary>
    /// <c>help Thing</c> for a class defined by an <c>@Thing</c> folder (stage P4, measured):
    /// "Thing methods:", a blank line, then one line per file of the folder - the class file as
    /// "Thing - is a class.", each method file by its H1 line when it has one, else "Thing/name is a
    /// function." (or "Thing.name is a function." for a static method) - the names right-aligned to
    /// the longest, the class file first and the rest by character code, then the "Documentation
    /// for" and "doc" lines.
    /// </summary>
    private static string ClassFolderHelp(JgsFunctionPath search, string classFile, string className)
    {
        JgsClass? loaded = LoadedClassOf(search, className);
        var entries = new List<(string Name, string Description)> { (className, H1Of(classFile) ?? "is a class.") };
        string folder = Path.GetDirectoryName(classFile) ?? string.Empty;
        foreach (string stem in search.Index.StemsOfFullPath(folder).Order(StringComparer.Ordinal))
        {
            if (string.Equals(stem, className, StringComparison.Ordinal))
            {
                continue;
            }

            bool isStatic = loaded is not null && loaded.TryMethod(stem, out ClassMethod? method) && method.Static;
            entries.Add((stem, H1Of(Path.Combine(folder, stem + ".m")) ?? $"{className}{(isStatic ? '.' : '/')}{stem} is a function."));
        }

        int width = entries.Max(static e => e.Name.Length);
        var text = new StringBuilder();
        text.Append($"{className} methods:\n\n");
        foreach ((string entryName, string description) in entries)
        {
            text.Append(entryName.PadLeft(width)).Append(" - ").Append(description).Append('\n');
        }

        text.Append($"\n    Documentation for {className}\n       doc {className}\n\n");
        return text.ToString();
    }

    /// <summary>
    /// <c>help Thing/extent</c>, <c>help Thing.scale</c> (stage P4, measured): the method file's
    /// comment block when it has one, else "Thing/extent is a function." with the header as written
    /// (<c>r = extent(o)</c>), or for a static method "Thing.staticOne is a function." with the
    /// header under the dotted name (<c>r = Thing.staticOne(x)</c>).
    /// </summary>
    private static string ClassFolderMethodHelp(JgsFunctionPath search, string methodFile, string className, string member)
    {
        if (string.Equals(member, className, StringComparison.Ordinal))
        {
            return FileHelp(search, methodFile, className);
        }

        string block = FileHelp(search, methodFile, member);
        if (!block.StartsWith($"{member} is a ", StringComparison.Ordinal))
        {
            return block; // the file's own comment block
        }

        bool isStatic = LoadedClassOf(search, className) is { } loaded && loaded.TryMethod(member, out ClassMethod? method) && method.Static;
        string header = FunctionLineOf(methodFile) is { } functionLine
            ? QualifiedHeader(functionLine, isStatic ? $"{className}.{member}" : member)
            : member;
        return isStatic
            ? $"{className}.{member} is a function.\n    {header}\n"
            : $"{className}/{member} is a function.\n    {header}\n";
    }

    /// <summary>The class <paramref name="className"/> names, loaded from the path if need be; null when it cannot be loaded (help still lists the folder).</summary>
    private static JgsClass? LoadedClassOf(JgsFunctionPath search, string className)
    {
        try
        {
            return search.Interpreter.ClassForLoad(className);
        }
        catch (JgsException)
        {
            return null;
        }
    }

    /// <summary>A file's H1 line - the text of the first comment line of its comment block, trimmed - or null when it has no block.</summary>
    private static string? H1Of(string file)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }

        int first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        if (first >= lines.Length)
        {
            return null;
        }

        int blockAt = lines[first].TrimStart().StartsWith('%') ? first : first + 1;
        return blockAt < lines.Length && lines[blockAt].TrimStart().StartsWith('%') ? lines[blockAt].TrimStart()[1..].Trim() : null;
    }

    /// <summary>The first <c>function</c> line of a code file, trimmed, or null.</summary>
    private static string? FunctionLineOf(string file)
    {
        try
        {
            foreach (string line in JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n'))
            {
                string code = line.TrimStart();
                if (StartsWithWord(code, "function"))
                {
                    return code;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// <c>help pkg</c>: "Contents of pkg namespace:", a blank line, then one line per member — the
    /// code files first, each "name - pkg.name is a function." (or a script, or a class), then the
    /// class folders as "+pkg\@D is a folder.", then the sub-packages as "pkg.sub is a namespace." —
    /// the names right-aligned to the longest, each group sorted ordinally, every folder holding
    /// the package merged (R2025b, measured).
    /// </summary>
    private static string PackageHelp(JgsFunctionPath search, IReadOnlyList<string> parts)
    {
        string qualified = string.Join('.', parts);
        (IReadOnlyList<string> members, IReadOnlyList<string> subPackages, IReadOnlyList<string> classFolders) = search.Tree.Contents(parts);
        var entries = new List<(string Name, string Description)>();
        foreach (string member in members)
        {
            string kind = search.FindDotted(qualified + "." + member, out _) is { } file ? KindOfFile(file) : "function";
            entries.Add((member, $"{qualified}.{member} is a {kind}."));
        }

        // A class folder is "+pkg\@D is a folder." while no class of the package has been loaded, and
        // "pkg.D is a class." once one has (R2025b, recorded both ways in p1_packages and p2_classes).
        string folderPrefix = string.Join(Path.DirectorySeparatorChar, parts.Select(static p => "+" + p));
        bool anyLoaded = search.Interpreter.Classes.Keys.Any(k => k.StartsWith(qualified + ".", StringComparison.Ordinal));
        foreach (string classFolder in classFolders)
        {
            entries.Add((classFolder, anyLoaded
                ? $"{qualified}.{classFolder} is a class."
                : $"{folderPrefix}{Path.DirectorySeparatorChar}@{classFolder} is a folder."));
        }

        foreach (string sub in subPackages)
        {
            entries.Add((sub, $"{qualified}.{sub} is a namespace."));
        }

        int width = entries.Count == 0 ? 0 : entries.Max(static e => e.Name.Length);
        var text = new StringBuilder();
        text.Append($"Contents of {qualified} namespace:\n\n");
        foreach ((string entryName, string description) in entries)
        {
            text.Append(entryName.PadLeft(width)).Append(" - ").Append(description).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A code file's help: its first comment block with each line's <c>%</c> read as a space (the
    /// block at the top of the file, or the one right under the function line), or, when it has
    /// none, "name is a function." with the header on the next line under four spaces, the name
    /// written as it was asked for (<c>r = pkg.f(x)</c>), or "name is a script." (R2025b, measured).
    /// </summary>
    private static string FileHelp(JgsFunctionPath search, string file, string asked)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{asked} is a function.\n";
        }

        int first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        if (first >= lines.Length)
        {
            return $"{asked} is a script.\n";
        }

        string head = lines[first].TrimStart();
        int blockAt = head.StartsWith('%') ? first : first + 1;
        var block = new StringBuilder();
        for (int i = blockAt; i < lines.Length && lines[i].TrimStart().StartsWith('%'); i++)
        {
            block.Append(' ').Append(lines[i].TrimStart()[1..]).Append('\n');
        }

        if (block.Length > 0)
        {
            return block.ToString();
        }

        if (StartsWithWord(head, "function"))
        {
            return $"{asked} is a function.\n    {QualifiedHeader(head, asked)}\n";
        }

        if (StartsWithWord(head, "classdef"))
        {
            // A class in an @Class folder lists the folder's files (stage P4, measured).
            return string.Equals(JgsCodeTree.ClassFolderOf(file), asked, StringComparison.Ordinal)
                ? ClassFolderHelp(search, file, asked)
                : ClassHelp(lines, asked);
        }

        return $"{asked} is a script.\n";
    }

    /// <summary>
    /// A class file's help when it has no comment block (R2025b, measured in P2): "pkg.C is a class."
    /// with the constructor's call form under the qualified name on the next line (<c>o = pkg.C(v)</c>,
    /// or <c>obj = pkg.C</c> for a class that writes no constructor), a blank line, and the
    /// "Documentation for" and "doc" lines.
    /// </summary>
    private static string ClassHelp(string[] lines, string asked)
    {
        string shortName = asked[(asked.LastIndexOf('.') + 1)..];
        string header = $"obj = {asked}";
        if (ClassSource(lines, asked) is { } parsed
            && parsed.Methods.FirstOrDefault(m => m.Function.Name == shortName) is { } constructor
            && constructor.Function.Line >= 1 && constructor.Function.Line <= lines.Length)
        {
            header = QualifiedHeader(lines[constructor.Function.Line - 1].TrimStart(), asked);
        }

        return $"{asked} is a class.\n    {header}\n\n    Documentation for {asked}\n       doc {asked}\n\n";
    }

    /// <summary>
    /// The help for a method of a class in a package (R2025b, measured in P2): an instance method
    /// as "pkg.C/m is a function." with its header as written (<c>r = m(o)</c>); a static method
    /// as "pkg.C.stat is a function." with the header under the dotted name
    /// (<c>r = pkg.C.stat(x)</c>); the constructor as "pkg.C/C is a constructor." with its header,
    /// then the "Documentation for pkg.C/C" and "doc pkg.C" lines. Null when the class has no
    /// method of the name, or the file is not a class.
    /// </summary>
    private static string? MethodHelp(string classFile, string owner, string methodName)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(classFile).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }

        if (ClassSource(lines, owner) is not { } parsed
            || parsed.Methods.FirstOrDefault(m => m.Function.Name == methodName) is not { } method
            || method.Function.Line < 1 || method.Function.Line > lines.Length)
        {
            return null;
        }

        string functionLine = lines[method.Function.Line - 1].TrimStart();
        string shortName = owner[(owner.LastIndexOf('.') + 1)..];
        if (methodName == shortName)
        {
            return $"{owner}/{methodName} is a constructor.\n    {QualifiedHeader(functionLine, methodName)}\n\n    Documentation for {owner}/{methodName}\n       doc {owner}\n\n";
        }

        return method.Static
            ? $"{owner}.{methodName} is a function.\n    {QualifiedHeader(functionLine, $"{owner}.{methodName}")}\n"
            : $"{owner}/{methodName} is a function.\n    {QualifiedHeader(functionLine, methodName)}\n";
    }

    /// <summary>The classdef a file's text declares, parsed for its methods' lines; null when the text does not parse as one.</summary>
    private static ClassdefStmt? ClassSource(string[] lines, string sourceId)
    {
        try
        {
            return Parser.Parse(string.Join('\n', lines), sourceId, JgsDialect.Matlab).OfType<ClassdefStmt>().FirstOrDefault();
        }
        catch (JgsException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>meta.package.fromName(name)</c> as JGraph answers it (the packages plan's decision PD5, ADR
    /// 0308): a struct with R2025b's property names - Name, Description (the name, as R2025b's is),
    /// DetailedDescription, ClassList (the package's class files and class folders, each a struct
    /// with its qualified Name), FunctionList (the code files that are not classes, scripts among
    /// them, measured), InnerNamespaces and PackageList (the sub-packages, qualified), OuterNamespace
    /// and ContainingPackage (the parent package's struct, or a 0-by-0 for a top one) - and a 0-by-0
    /// for a name no search folder holds as a package. The names come from the tree; nothing loads.
    /// </summary>
    internal static JgsValue PackageMeta(JgsFunctionPath? search, string name)
    {
        if (search is null || JgsCodeTree.DottedParts("a." + name) is null || !search.Tree.IsPackage(name.Split('.')))
        {
            return JgsEmpty.Zero();
        }

        string[] parts = name.Split('.');
        (IReadOnlyList<string> members, IReadOnlyList<string> subPackages, IReadOnlyList<string> classFolders) = search.Tree.Contents(parts);
        var classes = new List<string>();
        var functions = new List<string>();
        foreach (string member in members)
        {
            string kind = search.FindDotted(name + "." + member, out _) is { } file ? KindOfFile(file) : "function";
            (kind == "class" ? classes : functions).Add(kind == "class" ? $"{name}.{member}" : member);
        }

        classes.AddRange(classFolders.Select(folder => $"{name}.{folder}"));
        classes.Sort(StringComparer.Ordinal);
        JgsValue parent = parts.Length > 1 ? PackageMeta(search, string.Join('.', parts[..^1])) : NameList([]);
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(name),
            ["Description"] = JgsValue.Str(name),
            ["DetailedDescription"] = JgsValue.Str(string.Empty),
            ["ClassList"] = NameList(classes),
            ["FunctionList"] = NameList(functions),
            ["InnerNamespaces"] = NameList(subPackages.Select(sub => $"{name}.{sub}")),
            ["PackageList"] = NameList(subPackages.Select(sub => $"{name}.{sub}")),
            ["OuterNamespace"] = parent,
            ["ContainingPackage"] = parent,
        };
        JgsValue described = JgsValue.Struct(fields);
        described.SetClassName("meta.package");
        return described;

        static JgsValue NameList(IEnumerable<string> names)
        {
            Dictionary<string, JgsValue>[] rows = [.. names.Select(static n => new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Name"] = JgsValue.Str(n) })];
            return JgsValue.StructArray(new JgsStructArray(rows, ["Name"]), rows.Length, rows.Length == 0 ? 0 : 1);
        }
    }

    /// <summary>The function header's call form with the name as it was asked for: <c>function r = f(x)</c> → <c>r = pkg.f(x)</c>.</summary>
    private static string QualifiedHeader(string functionLine, string asked)
    {
        string header = functionLine["function".Length..].Trim();
        int equals = header.IndexOf('=');
        string outputs = equals >= 0 ? header[..equals].Trim() + " = " : string.Empty;
        string call = equals >= 0 ? header[(equals + 1)..].Trim() : header;
        int paren = call.IndexOf('(');
        string rest = paren >= 0 ? call[paren..] : string.Empty;
        return outputs + asked + rest;
    }

    /// <summary>What a code file is, read from its first line of code: a class, a function, or a script.</summary>
    internal static string KindOfFile(string file)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return "function"; // a P-file: its source is not there to read
        }

        foreach (string line in lines)
        {
            string code = line.TrimStart();
            if (code.Length == 0 || code.StartsWith('%'))
            {
                continue;
            }

            return StartsWithWord(code, "classdef") ? "class" : StartsWithWord(code, "function") ? "function" : "script";
        }

        return "script";
    }

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal)
        && (text.Length == word.Length || !(char.IsLetterOrDigit(text[word.Length]) || text[word.Length] == '_'));
}
