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
    ];

    /// <summary>The lines allowed to write raw, each with the reason the model is not broken by it.</summary>
    private static readonly (string File, string Line, string Why)[] Allowed =
    [
        ("Interpreter.cs",
         "widened.AsCell[row + (column * widened.Rows)] = value;",
         "the widened cell was allocated two statements above; nobody else holds it"),
        ("Interpreter.cs",
         "grown.AsCell[position] = value;",
         "the grown cell was allocated here, as the growth it is named for"),
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
         "clone.Fields[name] = interpreter.CopyForBinding(value);",
         "the clone's own fields; the source is untouched"),
        ("JgsBuiltins.Classes.cs",
         "built.AsStruct[\"cause\"] = JgsValue.Cell(causes);",
         "the MException struct was built in this method"),
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
