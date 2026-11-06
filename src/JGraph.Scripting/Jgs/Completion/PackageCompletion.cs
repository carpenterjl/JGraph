using JGraph.Scripting.Completion;

namespace JGraph.Scripting.Jgs.Completion;

/// <summary>
/// What the editor offers for a session's own packages and classes in a MATLAB buffer (the
/// packages and class folders plan, stage P5, ADR 0311): after <c>pkg.</c> the package's members -
/// its functions, scripts, classes, class folders and sub-packages, read off the code tree; after a
/// class's name (<c>pkg.C.</c>, <c>Thing.</c>) its statics, constants and enumeration members; and
/// after an instance (<c>obj.</c> in a class file or one of its method files) the class's
/// properties and methods, with an <c>@Class</c> folder's method files among them.
/// </summary>
/// <remarks>
/// Nothing here runs the script's code: a class that is loaded answers from its definition, and
/// one that is not from its file, parsed as text - a saved file parses, where a buffer mid-keystroke
/// need not. Called on the UI thread while the session may be running: it reads what it can and
/// answers less, never throws.
/// </remarks>
internal static class PackageCompletion
{
    /// <summary>
    /// The members after <paramref name="qualifier"/> and a dot when it names a package or a class the
    /// session's path can see; empty when it names neither, so the caller asks the .NET names.
    /// </summary>
    public static IReadOnlyList<CompletionItem> Members(Interpreter interpreter, string qualifier)
    {
        try
        {
            if (interpreter.FunctionPath is not { } path || !IsDottedName(qualifier))
            {
                return [];
            }

            string[] parts = qualifier.Split('.');
            JgsCodeTree tree = path.Tree;
            if (tree.IsPackage(parts))
            {
                return PackageMembers(path, qualifier, parts);
            }

            return StaticMembers(interpreter, path, qualifier, parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// The members an instance of <paramref name="className"/> offers (<c>obj.</c>): the class's listed
    /// properties and methods, and its folder's method files - from the loaded class, or from its file.
    /// </summary>
    public static IReadOnlyList<CompletionItem> ClassMembers(Interpreter interpreter, string className)
    {
        try
        {
            if (interpreter.FunctionPath is not { } path || !IsDottedName(className))
            {
                return [];
            }

            string[] parts = className.Split('.');
            if (interpreter.Classes.TryGetValue(className, out JgsClass? loaded))
            {
                var fromClass = new List<CompletionItem>();
                foreach (ClassProperty property in loaded.ListedProperties)
                {
                    fromClass.Add(new CompletionItem(property.Spec.Name, CompletionItemKind.Variable, Signature: null, $"property of {className}"));
                }

                foreach (ClassMethod method in loaded.ListedMethods.OrderBy(static m => m.Function.Name, StringComparer.Ordinal))
                {
                    fromClass.Add(MethodItem(method, className));
                }

                return fromClass;
            }

            if (ClassFile(path, parts) is not { } file)
            {
                return [];
            }

            var items = new List<CompletionItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Declaration(file) is { } declared)
            {
                // Every declared name is seen, so a private method's file is not offered as a public one below.
                foreach (ClassProperty property in declared.Properties)
                {
                    if (seen.Add(property.Spec.Name) && property.GetAccess.IsPublic && !property.Hidden)
                    {
                        items.Add(new CompletionItem(property.Spec.Name, CompletionItemKind.Variable, Signature: null, $"property of {className}"));
                    }
                }

                foreach (ClassMethod method in declared.Methods)
                {
                    if (seen.Add(method.Function.Name) && method.Access.IsPublic && !method.Hidden && !string.Equals(method.Function.Name, declared.Name, StringComparison.Ordinal))
                    {
                        items.Add(MethodItem(method, className));
                    }
                }
            }

            // The folder's method files, declared by a signature line or not (P4): each a public method.
            string shortName = parts[^1];
            if (JgsCodeTree.ClassFolderOf(file) is { } folderClass && folderClass == shortName
                && path.Tree.ClassFolder(shortName, parts.Length > 1 ? parts[..^1] : null) is { } hit)
            {
                foreach (string stem in hit.Entry.Stems.Order(StringComparer.Ordinal))
                {
                    if (stem != shortName && seen.Add(stem))
                    {
                        items.Add(new CompletionItem(stem, CompletionItemKind.Function, Signature: null, $"method file of {className}"));
                    }
                }
            }

            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    // --- a package's members ---------------------------------------------------------------------------

    private static IReadOnlyList<CompletionItem> PackageMembers(JgsFunctionPath path, string qualifier, string[] parts)
    {
        (IReadOnlyList<string> members, IReadOnlyList<string> subPackages, IReadOnlyList<string> classFolders) = path.Tree.Contents(parts);
        var items = new List<CompletionItem>();
        foreach (string sub in subPackages)
        {
            items.Add(new CompletionItem(sub, CompletionItemKind.Namespace, Signature: null, $"package {qualifier}.{sub}"));
        }

        foreach (string classFolder in classFolders)
        {
            items.Add(new CompletionItem(classFolder, CompletionItemKind.Type, Signature: null, $"class {qualifier}.{classFolder}"));
        }

        foreach (string member in members)
        {
            if (path.FindDotted($"{qualifier}.{member}", out _) is not { } file)
            {
                continue;
            }

            items.Add(JgsBuiltins.KindOfFile(file) switch
            {
                "class" => new CompletionItem(member, CompletionItemKind.Type, Signature: null, $"class {qualifier}.{member}"),
                "script" => new CompletionItem(member, CompletionItemKind.Function, Signature: null, $"script {qualifier}.{member}"),
                _ => new CompletionItem(member, CompletionItemKind.Function, Signature: null, $"function {qualifier}.{member}"),
            });
        }

        return items;
    }

    // --- a class's statics -----------------------------------------------------------------------------

    private static IReadOnlyList<CompletionItem> StaticMembers(Interpreter interpreter, JgsFunctionPath path, string className, string[] parts)
    {
        if (interpreter.Classes.TryGetValue(className, out JgsClass? loaded))
        {
            var fromClass = new List<CompletionItem>();
            foreach (string member in loaded.EnumerationNames)
            {
                fromClass.Add(new CompletionItem(member, CompletionItemKind.Member, Signature: null, $"enumeration member of {className}"));
            }

            foreach (ClassProperty property in loaded.ListedProperties)
            {
                if (property.Constant)
                {
                    fromClass.Add(new CompletionItem(property.Spec.Name, CompletionItemKind.Member, Signature: null, $"constant of {className}"));
                }
            }

            foreach (ClassMethod method in loaded.ListedMethods.Where(static m => m.Static).OrderBy(static m => m.Function.Name, StringComparer.Ordinal))
            {
                fromClass.Add(MethodItem(method, className));
            }

            return fromClass;
        }

        if (ClassFile(path, parts) is not { } file || Declaration(file) is not { } declared)
        {
            return [];
        }

        var items = new List<CompletionItem>();
        foreach (EnumerationMember member in declared.Enumeration)
        {
            items.Add(new CompletionItem(member.Name, CompletionItemKind.Member, Signature: null, $"enumeration member of {className}"));
        }

        foreach (ClassProperty property in declared.Properties)
        {
            if (property.Constant && property.GetAccess.IsPublic && !property.Hidden)
            {
                items.Add(new CompletionItem(property.Spec.Name, CompletionItemKind.Member, Signature: null, $"constant of {className}"));
            }
        }

        foreach (ClassMethod method in declared.Methods)
        {
            if (method.Static && method.Access.IsPublic && !method.Hidden)
            {
                items.Add(MethodItem(method, className));
            }
        }

        return items;
    }

    // --- the files ---------------------------------------------------------------------------------------

    /// <summary>The class file <paramref name="parts"/> names - <c>+pkg/C.m</c>, <c>+pkg/@D/D.m</c>, <c>@Thing/Thing.m</c> or <c>Thing.m</c> - or null.</summary>
    private static string? ClassFile(JgsFunctionPath path, string[] parts)
    {
        string? file = parts.Length > 1
            ? path.FindDotted(string.Join('.', parts), out _)
            : path.FindAll(parts[0]).FirstOrDefault();
        return file is not null && JgsBuiltins.KindOfFile(file) == "class" ? file : null;
    }

    /// <summary>The <c>classdef</c> of <paramref name="file"/>, parsed as text; null when the file does not parse.</summary>
    private static ClassdefStmt? Declaration(string file)
    {
        try
        {
            string text = JgsMlapp.ReadSource(file);
            IReadOnlyList<Token> tokens = Lexer.Tokenize(text, tolerant: false, JgsDialect.Matlab);
            return Parser.Parse(tokens, file, JgsDialect.Matlab).OfType<ClassdefStmt>().FirstOrDefault();
        }
        catch (Exception ex) when (ex is JgsSyntaxException or JgsRuntimeException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static CompletionItem MethodItem(ClassMethod method, string className) =>
        new(method.Function.Name, CompletionItemKind.Function,
            $"{method.Function.Name}({string.Join(", ", method.Function.Parameters)})",
            method.Static ? $"static method of {className}" : $"method of {className}");

    private static bool IsDottedName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (string part in name.Split('.'))
        {
            if (part.Length == 0 || !(char.IsLetter(part[0]) || part[0] == '_') || !part.All(static c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
