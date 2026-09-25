using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V1.3's tripwire (ADR 0162): no raw write into a payload, and no wrapper minted over one, except
/// where this list says why.
/// </summary>
/// <remarks>
/// M7 puts the holder check inside the setters, so a write that reaches storage another way is
/// exactly the hole the model cannot see. The spellings scanned here are the ones V1.1's audit
/// found: an indexed store through an accessor, a span store through a buffer or a plane, a
/// scatter into a payload, and a factory fed another value's payload. Each line that remains is
/// allowed by name and by reason, and the list is checked in both directions — an entry that no
/// longer matches any line fails too, so it cannot rot into permission for something else.
/// <para>
/// This is the fast guard that runs with the suite, and it reads one line at a time.
/// `tools/ownership/audit-ownership.py` is the census that follows a payload through the locals
/// that hold it; the gate runs both.
/// </para>
/// </remarks>
public class OwnershipGateTests
{
    private static readonly Regex[] Rules =
    [
        // An indexed store straight through an accessor.
        new(@"\.(AsArray|AsCell|AsStruct|Fields|BoxedElements\(\))\s*\[[^\]]*\]\s*(?<op>[-+*/|&^]?=)(?!=)"),

        // A store through a struct array's element dictionary.
        new(@"\.Elements\s*\[[^\]]*\]\s*\[[^\]]*\]\s*=(?!=)"),

        // A span store into a buffer or a complex plane.
        new(@"\.(AsBuffer|Re|Im)\s*\.AsSpan\(\)\s*\[[^\]]*\]\s*(?<op>[-+*/|&^]?=)(?!=)"),

        // A scatter or fill whose destination is reached through an accessor. These four take the
        // destination first; a kernel whose destination sits elsewhere in the argument list is the
        // Python audit's business, which reads each signature rather than guessing.
        new(@"PackedMath\.(Scatter|ScatterConstant|Fill|FillConstant)\(\s*[A-Za-z_][\w.]*\.(AsBuffer|AsPackedComplex)\b"),

        // A wrapper or payload class minted straight over another value's payload.
        new(@"(JgsValue\.(Packed|Shaped|PackedComplexArray|Cell|StructArray|Object|Array)|new\s+(JgsStructArray|JgsPackedComplex))\(\s*[A-Za-z_][\w.]*\.(AsBuffer|AsCell|AsArray|AsStruct|AsStructArray|AsPackedComplex|BoxedElements\(\)|Elements|Fields)\s*[,)]"),

        // V2.3 (ADR 0163): a workspace binding whose value is neither a fresh computation nor a
        // share. An entry holds a wrapper of its own (M2), so a Declare or TryAssign whose value
        // is not visibly minted (a JgsValue factory, an exception, an empty), copied
        // (CopyForBinding) or shared (Share) is a road the model has to explain by name.
        new(@"\.(Declare|DeclareFunction|TryAssign)\((?![^;]*(CopyForBinding\(|JgsValue\.\w+\(|MakeException\(|EmptyBracket\(|JgsMatrix\.\w+\())"),
    ];

    /// <summary>The lines allowed to write raw, each with the reason the model is not broken by it.</summary>
    private static readonly (string File, string Line, string Why)[] Allowed =
    [
        ("JgsValue.cs",
         "planes.Re.AsSpan()[index] = value.Real;",
         "inside SetPackedComplex, after Detach: this is M7's gate, not a road past it"),
        ("JgsValue.cs",
         "planes.Im.AsSpan()[index] = value.Imaginary;",
         "inside SetPackedComplex, after Detach: this is M7's gate, not a road past it"),
        ("JgsValue.cs",
         "copy.Fields[name] = Share(held);",
         "inside PrivateCopy: the fields being filled are the copy's own, and each child is shared"),
        ("JgsClass.cs",
         "instance.Fields[property.Spec.Name] = Check(property, start, line, col);",
         "a fresh instance's defaults, before anything can hold it"),
        ("JgsClass.cs",
         "instance.Fields[JgsBuiltins.EventNameField] = JgsValue.Str(string.Empty);",
         "event.EventData's inherited EventName, on the fresh instance two statements above (V6, #106)"),
        ("JgsClass.cs",
         "instance.Fields[JgsBuiltins.EventSourceField] = JgsMatrix.FromColumnMajor([], 0, 0);",
         "event.EventData's inherited Source, on the same fresh instance; the empty is minted here (V6, #106)"),
        ("JgsClass.cs",
         "clone.Fields[name] = interpreter.CopyForBinding(value);",
         "the clone's own fields; the source is untouched"),
        ("JgsWorkspaceIo.cs",
         "target.Fields[name] = target.Class.Check(property, value, line, col);",
         "a loaded object's saved property values, on the fresh instance load minted before reading them; each value is fresh from the file (V6, #111)"),
        ("JgsWorkspaceIo.cs",
         "defining.Declare(name, value);",
         "a loaded handle's captured workspace, declared into a static workspace minted for it; each value is fresh from the file (V6, #113)"),
        ("JgsBuiltins.Classes.cs",
         "built.AsStruct[\"cause\"] = column;",
         "the MException struct was built in this method, and the column is a cell minted here over shares of the causes (V6)"),
        ("Interpreter.cs",
         "handler.Declare(name, caught);",
         "the catch variable: an MException minted two lines above, by MakeException or by CaughtException over shares (V6)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"FrameCount\"] = JgsValue.Number(0);",
         "a videoWriter is a handle: its counters are meant to be seen through every name (V2 states it)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Duration\"] = JgsValue.Number(0);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Height\"] = JgsValue.Number(read.Height);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Width\"] = JgsValue.Number(read.Width);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"FrameCount\"] = JgsValue.Number(encoder.FrameCount);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Duration\"] = JgsValue.Number(encoder.FrameCount / FieldNumber(writer, \"FrameRate\"));",
         "a videoWriter is a handle (V2 states the contract)"),

        // --- V2.3: the bindings (Declare / TryAssign) whose value is not visibly fresh or shared ---
        ("Interpreter.HotLoop.cs",
         "if (!env.TryAssign(vslots[i].Name, value))",
         "a compiled loop's vector register, minted by the loop (ADR 0160) and written back once"),
        ("Interpreter.HotLoop.cs",
         "env.Declare(vslots[i].Name, value);",
         "as above: the loop's own register"),
        ("Interpreter.HotLoop.cs",
         "env.Declare(slots[i].Name, value);",
         "a compiled loop's scalar register: a fresh number"),
        ("Interpreter.HotLoop.cs",
         "else if (!env.TryAssign(slots[i].Name, value))",
         "as above: a fresh number"),
        ("Interpreter.cs",
         "env.Declare(spec.Name, value);",
         "an arguments-block default, evaluated here for this frame alone (a default naming another parameter is V9's call contract)"),
        ("Interpreter.cs",
         "env.Declare(spec.Name, checked_); // a declared class the value did not have yet",
         "the validator's converted value, minted by the conversion"),
        ("JgsEnvironment.cs",
         "slots.Declare(name, value);",
         "a declaration forwarded to the persistent's slot (M9): the caller's store, classified where it is made"),
        ("JgsEnvironment.cs",
         "this.Declare(name, value); // a nested function's own output, as the write's frame holds it",
         "a nested function's first write of its output, declared in its own frame with the wrapper the write hands in (V7)"),
        ("JgsEnvironment.cs",
         "target.Declare(name, value); // the parent's variable, as the write's own frame would hold it",
         "a nested function's write of a shared name, forwarded to the parent's frame with the wrapper the write's own frame would have declared (V7)"),
        ("JgsEnvironment.cs",
         "slots.Declare(name, value); // the slot is the binding, held or cleared",
         "an assignment forwarded to the persistent's slot (M9): the caller's store, classified where it is made"),
        ("Interpreter.cs",
         "env.Declare(let.Name, letValue);",
         "a JGS let: reference semantics (M17), the binding is the value's only holder"),
        ("Interpreter.cs",
         "env.Declare(destructure.Names[n], answers[n]);",
         "a JGS destructuring let over a call's answers (M17)"),
        ("Interpreter.cs",
         "env.Declare(destructure.Names[n], part);",
         "a JGS destructuring let over a tuple's parts (M17)"),
        ("Interpreter.cs",
         "env.Declare(\"ans\", bound);",
         "BindAns: `bound` is the adopted minted answer or CopyForBinding's share, decided one line above"),
        ("Interpreter.cs",
         "if (!scope.TryAssign(name, value))",
         "Rebind: every caller hands in a value it rebuilt (a time or table property, a grown cell, an atomic swap)"),
        ("Interpreter.cs",
         "scope.Declare(name, value);",
         "Rebind, as above"),
        ("Interpreter.cs",
         "local.Declare(statement.Variable, element);",
         "a for variable: the column the walk built, CopyForBinding's share of an element, or a stepped number"),
        ("Interpreter.cs",
         "if (!scope.TryAssign(variable.Name, stored))",
         "EvaluateAssign: `stored` is the adopted minted value or CopyForBinding's share, decided above"),
        ("Interpreter.cs",
         "scope.Declare(variable.Name, stored);",
         "EvaluateAssign, as above"),
        ("Interpreter.cs",
         "conjuredScope.Declare(conjuredName, EmptyOfKind(rhs));",
         "an empty conjured for a first indexed write"),
        ("Interpreter.Composite.cs",
         "scratch.Declare(LevelSlot, start);",
         "a fresh element of the struct array a new level belongs to (NewElement), in the scratch slot the rest of the path is written into (V6)"),
        ("Interpreter.TextWrites.cs",
         "scratch.Declare(LevelSlot, current);",
         "a char row minted as the 1-by-n char matrix it is, in the scratch slot the array roads grow (V6)"),
        ("Interpreter.Writes.cs",
         "scratch.Declare(LevelSlot, OneElementArray(scalar));",
         "a scalar an entry holds, minted as the one-by-one array it reads as, in the scratch slot the write grows (V6)"),
        ("Interpreter.Tables.cs",
         "scratch.Declare(LevelSlot, current);",
         "a table variable, fresh from its getter, in the scratch slot a brace write modifies and the rebuild takes back (V6)"),
        ("Interpreter.Composite.cs",
         "scratch.Declare(LevelSlot, slotValue);",
         "a computed level's value in the scratch slot the write modifies and its setter takes back (V6): fresh from its getter, or a share of what storage holds (a dictionary's cell value), which the slot's first write copies"),
        ("Interpreter.cs",
         "env.TryAssign(variable.Name, updated); // TryGet succeeded, so the binding exists",
         "a fresh number"),
        ("Interpreter.cs",
         "ScopeOf(variable.Name, env).Declare(variable.Name, created);",
         "a fresh empty struct"),
        ("Interpreter.cs",
         "ScopeOf(variable.Name, env).Declare(variable.Name, target);",
         "a fresh empty cell"),
        ("JgsBuiltinLayer.cs",
         "_root.Declare(name, value);",
         "a built-in constant registered once into the sealed layer"),
        ("JgsBuiltinLayer.cs",
         "_root.DeclareFunction(name, value);",
         "a built-in registered once into the sealed layer: a callable is immutable"),
        ("Interpreter.cs",
         "env.DeclareFunction(",
         "a hoisted function definition: a callable is immutable"),
        ("JgsClass.cs",
         "scope.DeclareFunction(Name, _constructor);",
         "a class constructor bound by name: a callable is immutable"),
        ("JgsCallable.cs",
         "snapshot.DeclareFunction(name, value);",
         "a captured function binding: a callable is immutable"),
        ("JgsWorkspaceIo.cs",
         "environment.Declare(name, value);",
         "load: the reader's fresh value, which the workspace adopts and the returned struct shares"),
        ("JgsWorkspaceIo.cs",
         "environment.Declare(name, value);",
         "load of a text file: a fresh matrix"),
    ];

    [Fact]
    public void NoRawWriteIntoAPayloadOutsideTheAllowList()
    {
        List<(string File, int Line, string Text)> found = Scan();
        var allowed = new HashSet<string>(
            Allowed.Select(a => Key(a.File, a.Line)), StringComparer.Ordinal);

        string[] unexplained = found
            .Where(hit => !allowed.Contains(Key(Path.GetFileName(hit.File), hit.Text)))
            .Select(hit => $"{Path.GetFileName(hit.File)}:{hit.Line}  {hit.Text}")
            .ToArray();

        Assert.True(
            unexplained.Length == 0,
            "A payload is written or adopted somewhere the ownership model cannot see (M7/M2). "
            + "Route it through the gated setters, or add it to OwnershipGateTests.Allowed with the "
            + "reason it is safe:" + Environment.NewLine + string.Join(Environment.NewLine, unexplained));
    }

    [Fact]
    public void EveryAllowedLineStillExists()
    {
        List<(string File, int Line, string Text)> found = Scan();
        var live = new HashSet<string>(
            found.Select(hit => Key(Path.GetFileName(hit.File), hit.Text)), StringComparer.Ordinal);

        string[] stale = Allowed
            .Where(a => !live.Contains(Key(a.File, a.Line)))
            .Select(a => $"{a.File}: {a.Line}")
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "An allowed raw write is no longer there. Remove the entry, so the list cannot become "
            + "permission for something else:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    private static string Key(string file, string line) => file + "|" + Normalize(line);

    private static string Normalize(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<(string File, int Line, string Text)> Scan()
    {
        var found = new List<(string, int, string)>();
        string root = Path.Combine(Repository(), "src", "JGraph.Scripting");
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = WithoutComment(lines[i]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (Regex rule in Rules)
                {
                    if (rule.IsMatch(code))
                    {
                        found.Add((path, i + 1, lines[i].Trim()));
                        break;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The line with any trailing comment removed, so prose about a write is not a write.</summary>
    private static string WithoutComment(string line)
    {
        var kept = new StringBuilder(line.Length);
        bool inString = false;
        char quote = '"';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (!inString && (c == '"' || c == '\''))
            {
                inString = true;
                quote = c;
            }
            else if (inString && c == quote && (i == 0 || line[i - 1] != '\\'))
            {
                inString = false;
            }

            kept.Append(c);
        }

        string text = kept.ToString().TrimStart();
        return text.StartsWith("///", StringComparison.Ordinal) || text.StartsWith("*", StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    private static string Repository()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "JGraph.sln")))
        {
            at = at.Parent;
        }

        return at?.FullName
            ?? throw new InvalidOperationException("the repository root is not above the test binaries");
    }
}
