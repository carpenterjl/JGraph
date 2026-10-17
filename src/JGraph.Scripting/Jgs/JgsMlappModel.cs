namespace JGraph.Scripting.Jgs;

/// <summary>What App Designer's copy of an app's code says, read for a check.</summary>
/// <param name="Fields">The names of the <c>code</c> variable's fields, in order.</param>
/// <param name="ClassName">The class name; null when the field is absent or not text.</param>
/// <param name="EditableSection">The editable section's lines; null when the field is absent.</param>
/// <param name="Callbacks">Each callback and its lines, in order; a null body is one that is not lines of text.</param>
/// <param name="Startup">The startup function and its lines; null when the field is absent.</param>
/// <param name="InputParameters">The startup function's parameters; null when the field is absent.</param>
internal sealed record MlappCodeCopy(
    IReadOnlyList<string> Fields,
    string? ClassName,
    IReadOnlyList<string>? EditableSection,
    IReadOnlyList<(string Name, IReadOnlyList<string>? Code)> Callbacks,
    (string Name, IReadOnlyList<string>? Code)? Startup,
    string? InputParameters);

/// <summary>
/// App Designer's design-time copy of an app's code, kept in step with the text (app-building
/// plan U7b, ADR 0205; the version 7.3 model, open item 57). An <c>.mlapp</c> holds the code
/// twice: the class file MATLAB runs, and in <c>appdesigner/appModel.mat</c> a variable <c>code</c>
/// that App Designer rebuilds its code view from. An edit written to the first alone runs, and is
/// thrown away the next time App Designer saves the app.
/// </summary>
/// <remarks>
/// <para>
/// The MAT-file's other variables are App Designer's component tree and a legacy object, both MCOS
/// objects. Nothing here decodes them. In a level-5 file every element but <c>code</c> is copied as
/// the bytes it is and the header's subsystem offset is moved to where its element now starts
/// (left stale, App Designer cannot load the file; measured in U0). In a version 7.3 file - HDF5,
/// which one shipped app and any app saved under MATLAB's version 7.3 preference keep - the new
/// <c>code</c> is appended past the end and the root group's entry re-pointed at it
/// (<see cref="MatFile.MatV73Splice"/>); not a byte of the rest moves.
/// </para>
/// <para>
/// <c>code</c> is a struct of plain values, read into one model of its fields whichever form the
/// file has (<see cref="CodeFields"/>), each field remembering how the file held it. A field is
/// rewritten only when the text it was cut from accounts for it: what the old text gives for the
/// field must be what the field holds, and then the field becomes what the new text gives. A field
/// the text never matched - a responsive app's layout callback, which App Designer records in
/// another form - is written back as App Designer wrote it, and so is any field this does not
/// know. A field is present exactly when it has something in it, which is App Designer's own rule.
/// </para>
/// </remarks>
internal static partial class JgsMlappModel
{
    private const string Variable = "code";

    /// <summary>
    /// Brings the MAT-file's <c>code</c> variable to what <paramref name="now"/> says. True when
    /// the copy then agrees with the text; false when it could not be made to - the text is not
    /// laid out as an app, the file has no <c>code</c> struct, or it is in a form that cannot be
    /// rewritten.
    /// </summary>
    /// <param name="mat">The bytes of <c>appModel.mat</c>.</param>
    /// <param name="before">The layout of the text the file was saved with.</param>
    /// <param name="now">The layout of the text being saved.</param>
    /// <param name="updated">The file to write in its place; null when it is to be left as it is.</param>
    /// <exception cref="InvalidDataException">The bytes are a MAT-file that ends inside an element.</exception>
    public static bool Update(byte[] mat, JgsMlappLayout before, JgsMlappLayout now, out byte[]? updated)
    {
        updated = null;
        if (!now.IsAppLayout || !now.IsComplete || Open(mat) is not { } copy)
        {
            return false;
        }

        if (Rewrite(copy.Fields, before, now))
        {
            updated = copy.Write();
        }

        return true;
    }

    /// <summary>What the MAT-file's <c>code</c> variable holds, or null when it has none that can be read.</summary>
    /// <exception cref="InvalidDataException">The bytes are a MAT-file that ends inside an element.</exception>
    public static MlappCodeCopy? Read(byte[] mat)
    {
        if (Open(mat) is not { } copy)
        {
            return null;
        }

        CodeFields code = copy.Fields;
        var callbacks = new List<(string, IReadOnlyList<string>?)>();
        if (code.Get("Callbacks") is { } stored && FunctionsOf(stored) is { } entries)
        {
            callbacks.AddRange(entries.Select(static e => (e.Name, e.Lines)));
        }

        (string, IReadOnlyList<string>?)? startup = null;
        if (code.Get("StartupCallback") is { } function && FunctionsOf(function) is { Count: 1 } one)
        {
            startup = (one[0].Name, one[0].Lines);
        }

        return new MlappCodeCopy(
            code.Names,
            code.Get("ClassName") is { } name ? TextOf(name) : null,
            code.Get("EditableSectionCode") is { } section ? LinesOf(section) : null,
            callbacks,
            startup,
            code.Get("InputParameters") is { } parameters ? TextOf(parameters) : null);
    }

    /// <summary>The file's <c>code</c> struct in whichever form the file keeps it; null when it has none that can be read or rewritten.</summary>
    private static ICodeCopy? Open(byte[] mat) => (ICodeCopy?)Level5Copy.Open(mat) ?? V73Copy.Open(mat);

    // --- The model -------------------------------------------------------------------------------

    /// <summary>The <c>code</c> struct of one file: its fields, and how to write them back in that file's form.</summary>
    private interface ICodeCopy
    {
        CodeFields Fields { get; }

        /// <summary>The whole MAT-file with the fields as they now are.</summary>
        byte[] Write();
    }

    /// <summary>
    /// A field's value as far as this reads it. <see cref="Origin"/> is how the file held it - a
    /// level-5 matrix element's bytes, a version 7.3 object's address - kept so that a value this
    /// did not change is written back exactly as it was; a value made here has none.
    /// </summary>
    private abstract record FieldValue(object? Origin);

    /// <summary>An empty value of any class, which is at once the empty text and no lines.</summary>
    private sealed record EmptyValue(object Origin) : FieldValue(Origin);

    /// <summary>A char row.</summary>
    private sealed record TextValue(string Text, object? Origin) : FieldValue(Origin);

    /// <summary>A cell of char rows; <paramref name="LineOrigins"/> is how the file held each line, where a form keeps lines as objects of their own.</summary>
    private sealed record LinesValue(IReadOnlyList<string> Lines, object? Origin, IReadOnlyList<object>? LineOrigins = null) : FieldValue(Origin);

    /// <summary>A struct array with the fields <c>Name</c> and <c>Code</c>: callbacks, or the startup function.</summary>
    private sealed record FunctionsValue(IReadOnlyList<FunctionEntry> Functions, object? Origin) : FieldValue(Origin);

    /// <summary>Anything else, written back as it was.</summary>
    private sealed record OpaqueValue(object Origin) : FieldValue(Origin);

    /// <summary>One function of a <c>Name</c>/<c>Code</c> array: its body as lines when it is lines, how the file held the body, and how it held each line where lines are objects of their own.</summary>
    private sealed record FunctionEntry(string Name, IReadOnlyList<string>? Lines, object? Body, IReadOnlyList<object>? LineOrigins = null);

    /// <summary>The struct's fields in order; a field is set by name, added at the end when new, and removed by a null.</summary>
    private sealed class CodeFields(List<(string Name, FieldValue Value)> fields)
    {
        public IReadOnlyList<string> Names => [.. fields.Select(static f => f.Name)];

        public IReadOnlyList<(string Name, FieldValue Value)> All => fields;

        public FieldValue? Get(string name)
        {
            int at = fields.FindIndex(f => f.Name == name);
            return at < 0 ? null : fields[at].Value;
        }

        public void Set(string name, FieldValue? value)
        {
            int at = fields.FindIndex(f => f.Name == name);
            if (value is null)
            {
                if (at >= 0)
                {
                    fields.RemoveAt(at);
                }
            }
            else if (at >= 0)
            {
                fields[at] = (name, value);
            }
            else
            {
                fields.Add((name, value));
            }
        }
    }

    /// <summary>A value as text; null when it is not a char row. An empty value is the empty text.</summary>
    private static string? TextOf(FieldValue value) => value switch
    {
        TextValue text => text.Text,
        EmptyValue => string.Empty,
        _ => null,
    };

    /// <summary>A value as lines; null when it is not a cell of char rows. An empty value is no lines.</summary>
    private static IReadOnlyList<string>? LinesOf(FieldValue value) => value switch
    {
        LinesValue lines => lines.Lines,
        EmptyValue => [],
        _ => null,
    };

    /// <summary>A value as functions; null when it is not a <c>Name</c>/<c>Code</c> array. An empty value is none.</summary>
    private static IReadOnlyList<FunctionEntry>? FunctionsOf(FieldValue value) => value switch
    {
        FunctionsValue functions => functions.Functions,
        EmptyValue => [],
        _ => null,
    };

    // --- Keeping the copy in step ----------------------------------------------------------------

    /// <summary>Brings each field the old text accounts for to what the new text gives. False when nothing changed.</summary>
    private static bool Rewrite(CodeFields code, JgsMlappLayout before, JgsMlappLayout now)
    {
        // Text saved while it could not be read left the copy behind it. There is then no old text
        // to hold a field against, and the copy is brought to the new text outright.
        bool told = before.IsAppLayout && before.IsComplete;
        bool changed = false;

        if (code.Get("ClassName") is { } name && TextOf(name) is { } className && (!told || className == before.ClassName)
            && now.ClassName != className && now.ClassName.Length > 0)
        {
            code.Set("ClassName", new TextValue(now.ClassName, null));
            changed = true;
        }

        IReadOnlyList<string>? section = code.Get("EditableSectionCode") is { } stored ? LinesOf(stored) : [];
        if (section is not null)
        {
            // Cut as the record was cut: with a Simulink app's generated block in it, when that is
            // the cut of the old text the record matches, and without otherwise.
            bool wide = told && !Same(section, before.EditableSectionCode) && Same(section, before.EditableSectionCodeWithGeneratedBlocks);
            IReadOnlyList<string> was = wide ? before.EditableSectionCodeWithGeneratedBlocks : before.EditableSectionCode;
            IReadOnlyList<string> cut = wide ? now.EditableSectionCodeWithGeneratedBlocks : now.EditableSectionCode;
            if ((!told || Same(section, was)) && !Same(section, cut))
            {
                code.Set("EditableSectionCode", cut.Count == 0 ? null : new LinesValue(cut, null));
                changed = true;
            }
        }

        changed |= RewriteCallbacks(code, told ? before : null, now);
        changed |= RewriteStartup(code, told ? before : null, now);

        string? parameters = code.Get("InputParameters") is { } held ? TextOf(held) : string.Empty;
        if (parameters is not null && (!told || parameters == before.InputParameters) && parameters != now.InputParameters)
        {
            code.Set("InputParameters", now.InputParameters.Length == 0 ? null : new TextValue(now.InputParameters, null));
            changed = true;
        }

        return changed;
    }

    private static bool RewriteCallbacks(CodeFields code, JgsMlappLayout? before, JgsMlappLayout now)
    {
        IReadOnlyList<FunctionEntry>? held = code.Get("Callbacks") is { } stored ? FunctionsOf(stored) : [];
        List<MlappRegion> was = before?.Callbacks.ToList() ?? [];
        if (held is null || (before is not null && !held.Select(static e => e.Name).SequenceEqual(was.Select(static r => r.Name))))
        {
            return false; // the old text does not account for the list
        }

        bool changed = false;
        var entries = new List<FunctionEntry>();
        foreach (MlappRegion region in now.Callbacks)
        {
            IReadOnlyList<string> lines = now.CodeOf(region);
            FunctionEntry? old = held.FirstOrDefault(e => e.Name == region.Name);
            MlappRegion? oldRegion = was.FirstOrDefault(r => r.Name == region.Name);

            // With no old text to hold it against, a body is App Designer's own when it is one
            // App Designer generates, and the user's otherwise.
            bool accounted = old?.Lines is not null && (before is null
                ? region.IsUserCode
                : oldRegion is not null && Same(old.Lines, before.CodeOf(oldRegion)));
            if (old is not null && (!accounted || Same(old.Lines!, lines)))
            {
                entries.Add(old); // as App Designer wrote it
            }
            else
            {
                entries.Add(new FunctionEntry(region.Name, lines, null));
                changed = true;
            }
        }

        changed |= !entries.Select(static e => e.Name).SequenceEqual(held.Select(static e => e.Name));
        if (changed)
        {
            code.Set("Callbacks", entries.Count == 0 ? null : new FunctionsValue(entries, null));
        }

        return changed;
    }

    private static bool RewriteStartup(CodeFields code, JgsMlappLayout? before, JgsMlappLayout now)
    {
        IReadOnlyList<FunctionEntry>? held = code.Get("StartupCallback") is { } stored ? FunctionsOf(stored) : [];
        if (held is null || held.Count > 1 || (held.Count == 1 && held[0].Lines is null))
        {
            return false;
        }

        if (before is not null)
        {
            MlappRegion? was = before.Startup;
            if ((held.Count == 1) != (was is not null)
                || (was is not null && (held[0].Name != was.Name || !Same(held[0].Lines!, before.CodeOf(was)))))
            {
                return false;
            }
        }

        MlappRegion? startup = now.Startup;
        if (startup is null)
        {
            if (held.Count == 0)
            {
                return false;
            }

            code.Set("StartupCallback", null);
            return true;
        }

        IReadOnlyList<string> lines = now.CodeOf(startup);
        if (held.Count == 1 && held[0].Name == startup.Name && Same(held[0].Lines!, lines))
        {
            return false;
        }

        code.Set("StartupCallback", new FunctionsValue([new FunctionEntry(startup.Name, lines, null)], null));
        return true;
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b, StringComparer.Ordinal);
}
