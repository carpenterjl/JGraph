using System.IO;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The search-path builtins (M62). Like <c>eval</c>'s family these need the running interpreter — the
/// path they manage <em>is</em> a piece of interpreter state, consulted whenever a name resolves to
/// nothing else — so they are declared beside them rather than with the pure ones.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Declares the path builtins and gives <paramref name="interpreter"/> the path itself. Every
    /// workspace owner calls this: a console session needs <c>addpath</c> to outlive one statement
    /// just as much as a batch run needs it to outlive one line.
    /// </summary>
    internal static void RegisterPathBuiltins(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        var search = new JgsFunctionPath(interpreter, host);
        interpreter.FunctionPath = search;

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // path and pathsep answer their bare names, the way pwd and filesep already do — MATLAB's
        // `path` on its own is how the search path is looked at, and handing back the function value
        // instead showed the user nothing at all. Callee position is exempt, so path(p) still sets.
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        // A relative folder means one beside the running script, not one beside whatever directory the
        // process happens to have been launched from. Every other file a script names already resolves
        // that way; addpath has to as well, or `addpath('lib')` works when you run the script from its
        // own folder and fails the moment a batch runner starts somewhere else — which is exactly how
        // this was found, by the stress runner and not by any probe run from inside the folder.
        string Locate(string folder) =>
            Path.IsPathRooted(folder) ? folder : Path.Combine(host.CurrentDirectory, folder);

        // The folders in search order as one string, which is what MATLAB's path is: the folder the
        // run is in, then whatever addpath added. The first entry is implicit and cannot be removed —
        // a script's own folder answering its own helper files is not a setting.
        string Joined() => string.Join(
            Path.PathSeparator,
            new[] { host.CurrentDirectory }.Concat(search.Folders).Distinct(StringComparer.OrdinalIgnoreCase));

        Query("path", (args, line, col) =>
        {
            // path(p) and path(p1, p2) replace the added folders wholesale, which is how a startup
            // file sets a path up in one line.
            if (args.Count > 0)
            {
                var replacement = new List<string>();
                for (int i = 0; i < args.Count; i++)
                {
                    replacement.AddRange(Split(Str("path", args, i, line, col)));
                }

                foreach (string folder in search.Folders.ToList())
                {
                    search.Remove(folder);
                }

                foreach (string folder in replacement)
                {
                    AddFolder(search, Locate(folder), atEnd: true, "path", line, col);
                }

                return JgsValue.Null;
            }

            return JgsValue.Str(Joined());
        });

        Define("addpath", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "addpath needs at least one folder.");
            }

            // A trailing '-end' appends instead of prepending; '-begin' says the default out loud.
            // '-frozen' is about caching MATLAB does and JGraph does not, so it is accepted and
            // ignored rather than refused — a script that passes it is not asking for anything.
            bool atEnd = false;
            var folders = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                string text = Str("addpath", args, i, line, col);
                switch (text)
                {
                    case "-end":
                        atEnd = true;
                        continue;
                    case "-begin" or "-frozen" or "-cache" or "-nocache":
                        continue;
                    default:
                        folders.AddRange(Split(text));
                        continue;
                }
            }

            // Added back to front when prepending, so addpath('a', 'b') leaves a before b.
            if (!atEnd)
            {
                folders.Reverse();
            }

            foreach (string folder in folders)
            {
                AddFolder(search, Locate(folder), atEnd, "addpath", line, col);
            }

            return JgsValue.Null;
        });

        Define("rmpath", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "rmpath needs at least one folder.");
            }

            for (int i = 0; i < args.Count; i++)
            {
                foreach (string folder in Split(Str("rmpath", args, i, line, col)))
                {
                    // MATLAB warns rather than fails here, and it is right to: removing a folder that
                    // is already absent has got the caller what they asked for.
                    if (!search.Remove(Locate(folder)))
                    {
                        host.WriteErr($"Warning: '{folder}' is not on the search path.\n");
                    }
                }
            }

            return JgsValue.Null;
        });

        Define("genpath", (args, line, col) =>
        {
            Arity("genpath", args, 1, line, col);
            string root = Locate(Str("genpath", args, 0, line, col));
            if (!Directory.Exists(root))
            {
                return JgsValue.Str(string.Empty); // MATLAB's answer for a folder that is not there
            }

            var found = new List<string> { Path.GetFullPath(root) };
            foreach (string sub in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                // MATLAB's genpath skips the folders that cannot hold code it would find this way.
                string leaf = Path.GetFileName(sub);
                if (leaf is not ("private" or ".git") && !leaf.StartsWith('@') && !leaf.StartsWith('+'))
                {
                    found.Add(Path.GetFullPath(sub));
                }
            }

            return JgsValue.Str(string.Join(Path.PathSeparator, found));
        });

        Query("pathsep", (args, line, col) =>
        {
            Arity("pathsep", args, 0, line, col);
            return JgsValue.Str(Path.PathSeparator.ToString());
        });

        // rehash is deliberately left where it already was, as the accepted no-op the session builtins
        // declare. It exists to drop MATLAB's cached view of the path, and there is nothing here to
        // drop: a function file is re-read whenever its timestamp has moved, so what rehash promises
        // is already true without it.
    }

    /// <summary>Adds one folder to <paramref name="search"/>, refusing a folder that is not there.</summary>
    private static void AddFolder(
        JgsFunctionPath search, string folder, bool atEnd, string builtin, int line, int col)
    {
        if (!Directory.Exists(folder))
        {
            throw new JgsRuntimeException(line, col, $"{builtin}: there is no folder '{folder}'.");
        }

        search.Add(folder, atEnd);
    }

    /// <summary>Splits a path-separator-joined list, which every path builtin accepts wherever it takes a folder.</summary>
    private static IEnumerable<string> Split(string text) =>
        text.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
