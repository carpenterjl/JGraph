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
        // f.m, f.p: the file the name with its extension means (open item 174, measured).
        string extension = Path.GetExtension(name);
        if ((extension.Equals(".m", StringComparison.OrdinalIgnoreCase) || extension.Equals(JgsPcode.Extension, StringComparison.OrdinalIgnoreCase))
            && search.FindFile(name) is { } named)
        {
            return FileHelp(search, named, name[..^extension.Length]);
        }

        // f>g: a local function of the file f (open item 174, measured).
        int local = name.IndexOf('>');
        if (local > 0)
        {
            return search.Find(name[..local]) is { } host && !JgsPcode.IsPcode(host) ? LocalFunctionHelp(host, name[(local + 1)..]) : null;
        }

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

        // C/m, C/P: a method or property of a class file (open item 174, measured).
        if (JgsFunctionPath.SlashForm(name) is { } member && search.Find(member.Class) is { } ownerFile && KindOfFile(ownerFile) == "class")
        {
            return MethodHelp(ownerFile, member.Class, member.Member);
        }

        // folder/f, folder/f.m: a code file named by a folder on the path (open item 174, measured).
        if (name.Contains('/'))
        {
            bool withExtension = name.EndsWith(".m", StringComparison.OrdinalIgnoreCase);
            if ((withExtension || !name.Contains('.')) && search.FindRelative(withExtension ? name : name + ".m") is { } relative)
            {
                return FileHelp(search, relative, Path.GetFileNameWithoutExtension(relative));
            }
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

            // C.m, C.P: a method or property of a class file (open item 174, measured).
            if (parts.Length == 2 && search.Find(parts[0]) is { } classOwner && KindOfFile(classOwner) == "class")
            {
                return MethodHelp(classOwner, parts[0], parts[1]);
            }

            return search.Tree.IsPackage(parts) ? PackageHelp(search, parts) : null;
        }

        if (JgsCodeTree.DottedParts("a." + name) is null)
        {
            return null; // not an identifier: a path or an expression, which help has no answer for
        }

        if (search.Find(name) is { } file)
        {
            // A file whose name differs only in case (Windows finds it): R2025b says so and shows its help (measured).
            string onDisk = Path.GetFileNameWithoutExtension(DiskName(file));
            return string.Equals(onDisk, name, StringComparison.Ordinal)
                ? FileHelp(search, file, name)
                : $"--- {name} not found. Showing help for {onDisk} instead. ---\n\n" + FileHelp(search, file, onDisk);
        }

        return search.Tree.IsPackage([name]) ? PackageHelp(search, [name]) : FolderHelp(search, name);
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
    /// A code file's help (R2025b, measured in P1 and open item 174): its comment block - the one at the
    /// top of the file, or else the one under the function or classdef line, past the header's continued
    /// lines and any blank lines - each line's leading <c>%</c> (or <c>%%</c>) read as a space; a class's
    /// block followed by the "Documentation for" and "doc" lines. With no block, "name is a function."
    /// with the header on the next line under four spaces when it says more than the name, the name
    /// written as it was asked for (<c>r = pkg.f(x)</c>), or "name is a script.", or the class's form. A
    /// P-file's help is its source's beside it (<see cref="PcodeHelp"/>).
    /// </summary>
    private static string FileHelp(JgsFunctionPath search, string file, string asked)
    {
        if (JgsPcode.IsPcode(file))
        {
            return PcodeHelp(search, file, asked);
        }

        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{asked} is a function.\n";
        }

        int first = SkipBlankLines(lines, 0);
        if (first >= lines.Length)
        {
            return $"{asked} is a script.\n";
        }

        string head = lines[first].TrimStart();
        int declaration = FirstCodeLine(lines, first);
        string code = declaration < lines.Length ? lines[declaration].TrimStart() : string.Empty;
        bool isFunction = StartsWithWord(code, "function");
        bool isClass = StartsWithWord(code, "classdef");
        string block = head.StartsWith('%') ? HelpBlock(lines, first)
            : isFunction || isClass ? BlockAfterDeclaration(lines, declaration)
            : string.Empty;

        if (block.Length > 0)
        {
            return isClass ? block + $"\n    Documentation for {asked}\n       doc {asked}\n\n" : block;
        }

        if (isFunction)
        {
            string header = QualifiedHeader(HeaderText(lines, declaration), asked);
            return header == asked ? $"{asked} is a function.\n" : $"{asked} is a function.\n    {header}\n";
        }

        if (isClass)
        {
            // A class in an @Class folder lists the folder's files (stage P4, measured).
            return string.Equals(JgsCodeTree.ClassFolderOf(file), asked, StringComparison.Ordinal)
                ? ClassFolderHelp(search, file, asked)
                : ClassHelp(lines, asked);
        }

        return $"{asked} is a script.\n";
    }

    /// <summary>
    /// A P-file's help (open item 174, measured): the help of the <c>.m</c> beside it, as that file's
    /// own; with no source, only what the P-file says it is - "name is a function." with no header,
    /// "name is a script.", or "name is a class." with the documentation lines - since its comments
    /// are gone.
    /// </summary>
    private static string PcodeHelp(JgsFunctionPath search, string file, string asked)
    {
        string source = Path.ChangeExtension(file, ".m");
        if (File.Exists(source))
        {
            return FileHelp(search, source, asked);
        }

        return KindOfFile(file) switch
        {
            "script" => $"{asked} is a script.\n",
            "class" => $"{asked} is a class.\n\n    Documentation for {asked}\n       doc {asked}\n\n",
            _ => $"{asked} is a function.\n",
        };
    }

    /// <summary>A file's name as the folder spells it, which a lookup ignoring case may not have.</summary>
    private static string DiskName(string file)
    {
        try
        {
            string? folder = Path.GetDirectoryName(file);
            return folder is null ? file : Directory.GetFiles(folder, Path.GetFileName(file)).FirstOrDefault() ?? file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return file;
        }
    }

    /// <summary>The first line at or after <paramref name="at"/> that holds anything but white space; the line count when there is none.</summary>
    private static int SkipBlankLines(string[] lines, int at)
    {
        while (at < lines.Length && lines[at].Trim().Length == 0)
        {
            at++;
        }

        return at;
    }

    /// <summary>The first line at or after <paramref name="at"/> that is code: neither blank nor a comment.</summary>
    private static int FirstCodeLine(string[] lines, int at)
    {
        while (at < lines.Length && (lines[at].Trim().Length == 0 || lines[at].TrimStart().StartsWith('%')))
        {
            at++;
        }

        return at;
    }

    /// <summary>
    /// The comment block that starts at <paramref name="at"/>, as help prints it: each line up to the
    /// first that is not a comment (a blank line ends it), its leading <c>%</c> - or <c>%%</c>, as one -
    /// read as a space (R2025b, measured: <c>%%two</c> is " two", <c>%%%three</c> " %three", and
    /// <c>%{</c> is a line reading " {" that the next ends). Empty when no comment starts there.
    /// </summary>
    private static string HelpBlock(string[] lines, int at)
    {
        var block = new List<string>();
        for (int i = at; i < lines.Length && lines[i].TrimStart().StartsWith('%'); i++)
        {
            string comment = lines[i].TrimStart();
            block.Add(' ' + (comment.StartsWith("%%", StringComparison.Ordinal) ? comment[2..] : comment[1..]));
        }

        // An empty comment line at the block's end is left out; one inside it stays (measured).
        while (block.Count > 0 && block[^1].Trim().Length == 0)
        {
            block.RemoveAt(block.Count - 1);
        }

        return block.Count == 0 ? string.Empty : string.Join('\n', block) + "\n";
    }

    /// <summary>The comment block under a function, method or classdef line: past the header's continued lines and any blank lines (measured).</summary>
    private static string BlockAfterDeclaration(string[] lines, int declaration)
    {
        int at = declaration;
        while (at < lines.Length && CodePart(lines[at]).TrimEnd().EndsWith("...", StringComparison.Ordinal))
        {
            at++;
        }

        return HelpBlock(lines, SkipBlankLines(lines, at + 1));
    }

    /// <summary>A declaration line with its continued lines joined and its comment dropped: <c>function r = f(a, ...</c> / <c>b)</c> reads <c>function r = f(a, b)</c>.</summary>
    private static string HeaderText(string[] lines, int declaration)
    {
        var text = new StringBuilder();
        for (int at = declaration; at < lines.Length; at++)
        {
            string code = CodePart(lines[at]).Trim();
            if (!code.EndsWith("...", StringComparison.Ordinal))
            {
                return text.Append(code).ToString();
            }

            text.Append(code[..^3].TrimEnd()).Append(' ');
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>A line's code before its comment: up to the first <c>%</c> outside quotes (a quote after a letter, digit, bracket or quote is a transpose).</summary>
    private static string CodePart(string line)
    {
        char? open = null;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (open is { } quote)
            {
                if (c == quote)
                {
                    open = null;
                }
            }
            else if (c == '%')
            {
                return line[..i];
            }
            else if (c == '"' || (c == '\'' && (i == 0 || !(char.IsLetterOrDigit(line[i - 1]) || line[i - 1] is '_' or ')' or ']' or '}' or '\'' or '.'))))
            {
                open = c;
            }
        }

        return line;
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
    /// The help for a method of a class file (R2025b, measured in P2 and open item 174): the comment
    /// block under the method's function line when it has one - the constructor's followed by the
    /// "Documentation for C/C" and "doc C" lines; else an instance method as "C/m is a function." with
    /// its header as written (<c>r = m(o)</c>); a static method as "C.stat is a function." with the
    /// header under the dotted name (<c>r = C.stat(x)</c>); the constructor as "C/C is a constructor."
    /// with its header, then the "Documentation for C/C" and "doc C" lines. A property's help when the
    /// class has a property of the name and no such method (<see cref="PropertyHelp"/>). Null when the
    /// class has neither, or the file is not a class.
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

        if (ClassSource(lines, owner) is not { } parsed)
        {
            return null;
        }

        if (parsed.Methods.FirstOrDefault(m => m.Function.Name == methodName) is not { } method
            || method.Function.Line < 1 || method.Function.Line > lines.Length)
        {
            return parsed.Properties.FirstOrDefault(p => p.Spec.Name == methodName) is { } property
                ? PropertyHelp(lines, owner, property)
                : null;
        }

        string functionLine = HeaderText(lines, method.Function.Line - 1).TrimStart();
        string block = BlockAfterDeclaration(lines, method.Function.Line - 1);
        string shortName = owner[(owner.LastIndexOf('.') + 1)..];
        if (methodName == shortName)
        {
            return block.Length > 0
                ? $"{block}\n    Documentation for {owner}/{methodName}\n       doc {owner}\n\n"
                : $"{owner}/{methodName} is a constructor.\n    {QualifiedHeader(functionLine, methodName)}\n\n    Documentation for {owner}/{methodName}\n       doc {owner}\n\n";
        }

        if (block.Length > 0)
        {
            return block;
        }

        return method.Static
            ? $"{owner}.{methodName} is a function.\n    {QualifiedHeader(functionLine, $"{owner}.{methodName}")}\n"
            : $"{owner}/{methodName} is a function.\n    {QualifiedHeader(functionLine, methodName)}\n";
    }

    /// <summary>
    /// <c>help C.P</c> for a property (open item 174, measured): the comment block right above its
    /// line, else " P - " and the comment at the end of its line, else "C/P is a property.".
    /// </summary>
    private static string PropertyHelp(string[] lines, string owner, ClassProperty property)
    {
        int at = property.Spec.Line - 1;
        if (at >= 0 && at < lines.Length)
        {
            int above = at;
            while (above > 0 && lines[above - 1].TrimStart().StartsWith('%'))
            {
                above--;
            }

            if (above < at)
            {
                return HelpBlock(lines, above);
            }

            string code = CodePart(lines[at]);
            if (code.Length < lines[at].Length)
            {
                string comment = lines[at][code.Length..];
                return $" {property.Spec.Name} - {(comment.StartsWith("%%", StringComparison.Ordinal) ? comment[2..] : comment[1..])}\n";
            }
        }

        return $"{owner}/{property.Spec.Name} is a property.\n";
    }

    /// <summary>
    /// <c>help f&gt;g</c> (open item 174, measured): the comment block under the local function's line,
    /// else "g is a local function." with its header; null when the file has no local function of
    /// the name.
    /// </summary>
    private static string? LocalFunctionHelp(string file, string name)
    {
        string[] lines;
        IReadOnlyList<FnStmt> functions;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
            functions = [.. Parser.Parse(string.Join('\n', lines), file, JgsDialect.Matlab).OfType<FnStmt>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JgsException)
        {
            return null;
        }

        if (functions.Skip(1).FirstOrDefault(f => f.Name == name) is not { } local || local.Line < 1 || local.Line > lines.Length)
        {
            return null;
        }

        string block = BlockAfterDeclaration(lines, local.Line - 1);
        if (block.Length > 0)
        {
            return block;
        }

        string header = QualifiedHeader(HeaderText(lines, local.Line - 1).TrimStart(), name);
        return header == name ? $"{name} is a local function.\n" : $"{name} is a local function.\n    {header}\n";
    }

    /// <summary>
    /// <c>help folder</c> for a folder on the search path, or under a search folder (open item 174,
    /// measured): its <c>Contents.m</c>'s help when it has one; else "Contents of folder:", a blank
    /// line, and one line per code file - its H1 line with the file's name at its start left out, or
    /// "is a function." (a script, a class) - the names right-aligned to the longest, ordered by
    /// character code; "folder is a folder." when it holds no code file. Null when no such folder.
    /// </summary>
    private static string? FolderHelp(JgsFunctionPath search, string name)
    {
        if (name.Length == 0 || name.IndexOfAny(['.', '>', '@', '+']) >= 0)
        {
            return null;
        }

        string? folder = null;
        foreach (string searched in search.Index.SearchFolders)
        {
            if (string.Equals(Path.GetFileName(searched.TrimEnd('\\', '/')), name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(searched))
            {
                folder = searched;
                break;
            }

            string under = Path.Combine(searched, name);
            if (Directory.Exists(under))
            {
                folder ??= under;
            }
        }

        if (folder is null)
        {
            return null;
        }

        string contents = Path.Combine(folder, "Contents.m");
        if (File.Exists(contents))
        {
            string block = FileHelp(search, contents, "Contents");
            if (!block.StartsWith("Contents is a ", StringComparison.Ordinal))
            {
                return block;
            }
        }

        string[] files;
        try
        {
            files = [.. Directory.GetFiles(folder, "*.m").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string shown = Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (files.Length == 0)
        {
            return $"{shown} is a folder.\n";
        }

        var entries = new List<(string Name, string Description)>();
        foreach (string stem in files)
        {
            string file = Path.Combine(folder, stem + ".m");
            string? h1 = H1Of(file);
            if (h1 is not null)
            {
                int space = h1.IndexOfAny([' ', '\t']);
                string firstWord = space < 0 ? h1 : h1[..space];
                if (string.Equals(firstWord, stem, StringComparison.OrdinalIgnoreCase))
                {
                    h1 = space < 0 ? string.Empty : h1[space..].Trim();
                }
            }

            entries.Add((stem, h1 is { Length: > 0 } ? h1 : $"is a {KindOfFile(file)}."));
        }

        int width = entries.Max(static e => e.Name.Length);
        var text = new StringBuilder();
        text.Append($"Contents of {shown}:\n\n");
        foreach ((string entryName, string description) in entries)
        {
            text.Append(entryName.PadLeft(width)).Append(" - ").Append(description).Append('\n');
        }

        return text.ToString();
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
        // A P-file says what it is by its first statement (open item 174: R2025b's help knows a
        // P-file script for a script with no source beside it, measured).
        if (JgsPcode.IsPcode(file))
        {
            try
            {
                return JgsPcode.Load(file).FirstOrDefault() switch
                {
                    ClassdefStmt => "class",
                    FnStmt => "function",
                    null => "script",
                    _ => "script",
                };
            }
            catch (Exception ex) when (ex is JgsException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return "function";
            }
        }

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
