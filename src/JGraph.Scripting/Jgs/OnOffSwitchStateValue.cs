using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.lang.OnOffSwitchState</c> (open item 129, probe_129 and probe_129b): R2025b's enumeration
/// with the members <c>off</c> and <c>on</c>, a subclass of <c>logical</c>. A value is an array of
/// members, column-major, shaped like any array of external values, so indexing, growth, the shape
/// verbs and the bracket are the external array machinery's. What the class answers itself - its
/// names, its displays, <c>==</c> against text, <c>~</c> - is here; everything it inherits from
/// <c>logical</c> is asked of the logical values the members are.
/// </summary>
internal sealed class OnOffSwitchStateValue : IJgsExternalArray
{
    /// <summary>The class's name, as <c>class</c> answers it.</summary>
    public const string Name = "matlab.lang.OnOffSwitchState";

    /// <summary>The name a display shows: the class without its package.</summary>
    public const string ShortName = "OnOffSwitchState";

    private OnOffSwitchStateValue(bool[] states, int rows, int columns)
    {
        States = states;
        Rows = rows;
        Columns = columns;
    }

    /// <summary>Each member, column-major: true for <c>on</c>.</summary>
    public bool[] States { get; }

    public int Rows { get; }

    public int Columns { get; }

    public string ClassName => Name;

    public bool IsHandle => false;

    public string Kind => "enumeration member";

    /// <summary>Whether every member is <c>on</c>, and there is one at least: what <c>if</c> and <c>while</c> ask.</summary>
    public bool AllOn => States.Length > 0 && Array.TrueForAll(States, static s => s);

    public bool IsA(string className) => className is Name or "logical";

    public IJgsExternal CopyForBinding() => this;

    /// <summary>A member's name.</summary>
    public static string NameOf(bool state) => state ? "on" : "off";

    /// <summary>
    /// Whether this thread has made a member: a call with one among its arguments must look for the
    /// class's methods, which the interpreter only does once it knows external values are about.
    /// </summary>
    [ThreadStatic]
    private static bool t_minted;

    /// <summary>Whether this thread has made a member (open item 132).</summary>
    public static bool MintedOnThread => t_minted;

    /// <summary>One member as a script value.</summary>
    public static JgsValue Of(bool state)
    {
        t_minted = true;
        return JgsValue.External(new OnOffSwitchStateValue([state], 1, 1));
    }

    /// <summary>Members in a shape, as a script value.</summary>
    public static JgsValue Of(bool[] states, int rows, int columns)
    {
        t_minted = true;
        return JgsValue.External(new OnOffSwitchStateValue(states, rows, columns));
    }

    /// <summary>The members as the logical array they are underneath, in their shape.</summary>
    public JgsValue Logical()
    {
        if (States.Length == 1)
        {
            return JgsValue.Bool(States[0]);
        }

        JgsValue mask = JgsValue.Array([.. States.Select(JgsValue.Bool)]);
        mask.Reshape(Rows, Columns);
        return mask;
    }

    /// <summary>The members' names, column-major.</summary>
    public string[] Names => [.. States.Select(NameOf)];

    public IJgsExternalArray Build(IReadOnlyList<(IJgsExternalArray Source, int Index)?> elements, int rows, int columns)
    {
        var states = new bool[rows * columns];
        for (int i = 0; i < states.Length; i++)
        {
            // A gap growth fills takes the first member the enumeration lists, off (probe_129b).
            states[i] = elements[i] is ({ } source, int index) && ((OnOffSwitchStateValue)source).States[index];
        }

        return new OnOffSwitchStateValue(states, rows, columns);
    }

    /// <summary>Text, a logical or a number joined to members, or stored into them, becomes the member it names.</summary>
    public IJgsExternalArray Coerce(JgsValue value, int line, int col) => (IJgsExternalArray)Construct([value], line, col).AsExternal;

    public JgsValue GetMember(int index, string name, int line, int col) =>
        throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class '{Name}'.");

    public IJgsExternalArray WithMember(int index, string name, JgsValue value, int line, int col) =>
        throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass", $"Unrecognized property '{name}' for class '{Name}'.");

    public bool SameAs(IJgsExternalArray other) =>
        other is OnOffSwitchStateValue that && that.Rows == Rows && that.Columns == Columns && that.States.AsSpan().SequenceEqual(States);

    /// <summary><c>disp</c>: each row of names, each column padded to its longest, four spaces before each, a blank line after.</summary>
    public string Disp() => States.Length == 0 ? string.Empty : Grid() + "\n";

    /// <summary>
    /// The block a display shows under its name: the header, its class name a bold link to the class's
    /// help as R2025b writes it, a blank line, the names (probe_129b).
    /// </summary>
    public string Display()
    {
        string linked = $"<a href=\"matlab:helpPopup('{Name}')\" style=\"font-weight:bold\">{ShortName}</a>";
        return States.Length == 0
            ? $"  {Rows}×{Columns} empty {linked} enumeration."
            : States.Length == 1
                ? $"  {linked} enumeration\n\n{Grid()}"
                : $"  {Rows}×{Columns} {linked} enumeration array\n\n{Grid()}";
    }

    public string? Summary() => States.Length == 1 ? NameOf(States[0]) : null;

    /// <summary>The names in rows, every one padded to the longest in the array: <c>on     off</c>, <c>on </c> over <c>off</c>.</summary>
    private string Grid()
    {
        int width = States.Length == 0 ? 0 : States.Max(static s => NameOf(s).Length);
        var lines = new List<string>();
        for (int r = 0; r < Rows; r++)
        {
            var row = new StringBuilder();
            for (int c = 0; c < Columns; c++)
            {
                row.Append("    ").Append(NameOf(States[(c * Rows) + r]).PadRight(width));
            }

            lines.Add(row.ToString());
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The member a piece of text names, without regard to case as R2025b's conversion reads it
    /// (<c>'On'</c> is on), or null when it names neither.
    /// </summary>
    public static bool? Named(string text) => text.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };

    /// <summary>R2025b's refusal of text that names no member.</summary>
    public static JgsRuntimeException NotAMember(string text, int line, int col) =>
        new(line, col, "MATLAB:class:CannotConvert",
            $"'{text}' is not a member of enumeration '{Name}'. Value must be one of:\n    off\n    on\n");

    /// <summary>
    /// <c>matlab.lang.OnOffSwitchState(x)</c>: members by name (a char row, a string array, a cell of
    /// names) or by truth (a logical or a number, nonzero being on; NaN refuses as a logical
    /// conversion does), in the shape given.
    /// </summary>
    public static JgsValue Construct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:OutsideEnumBlock",
                $"Input does not correspond to a member of the enumeration class '{Name}'.");
        }

        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue given = args[0];
        if (given.AsExternalOrNull() is OnOffSwitchStateValue already)
        {
            return JgsValue.External(already);
        }

        bool FromText(JgsValue text)
        {
            string written = text.AsString;
            return Named(written) ?? throw NotAMember(written, line, col);
        }

        if (given.Type == JgsType.String && !given.IsStringArray)
        {
            return Of(FromText(given));
        }

        if (given.IsStringArray || given.Type == JgsType.Cell)
        {
            JgsValue[] names = given.Type == JgsType.Cell ? given.AsCell : given.BoxedElements();
            var states = new bool[names.Length];
            for (int i = 0; i < states.Length; i++)
            {
                if (names[i].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:class:CannotConvert",
                        $"Unable to convert the input to an enumeration member of class '{Name}'.");
                }

                states[i] = FromText(names[i]);
            }

            return Of(states, JgsMatrix.RowCount(given), JgsMatrix.ColCount(given));
        }

        if (given.Type is JgsType.Number or JgsType.Bool)
        {
            return double.IsNaN(given.AsNumber)
                ? throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.")
                : Of(given.AsNumber != 0);
        }

        if (given.Type == JgsType.Array && !given.IsCategorical && !given.IsCharMatrix)
        {
            int count = given.ArrayLength;
            var states = new bool[count];
            for (int i = 0; i < count; i++)
            {
                double value = given.ElementAt(i).AsNumber;
                if (double.IsNaN(value))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");
                }

                states[i] = value != 0;
            }

            return count == 0 ? Of([], 0, 0) : Of(states, JgsMatrix.RowCount(given), JgsMatrix.ColCount(given));
        }

        throw new JgsRuntimeException(line, col, "MATLAB:class:CannotConvert",
            $"Unable to convert the input to an enumeration member of class '{Name}'.");
    }

    /// <summary>
    /// <c>==</c> element by element against members, text (a name, a string array, a cell of names:
    /// one naming no member is equal to nothing), or a logical or numbers (the members' truth): a
    /// logical array of the shape of whichever side is not a scalar. Null when the shapes do not agree.
    /// </summary>
    public JgsValue? Equal(JgsValue other)
    {
        double[] theirs;
        int rows;
        int columns;
        double Code(JgsValue text) => text.Type == JgsType.String && Named(text.AsString) is bool named ? (named ? 1 : 0) : double.NaN;
        if (other.AsExternalOrNull() is OnOffSwitchStateValue members)
        {
            (theirs, rows, columns) = ([.. members.States.Select(static s => s ? 1.0 : 0.0)], members.Rows, members.Columns);
        }
        else if (other.Type == JgsType.String && !other.IsStringArray)
        {
            (theirs, rows, columns) = ([Code(other)], 1, 1);
        }
        else if (other.IsStringArray || other.Type == JgsType.Cell)
        {
            JgsValue[] names = other.Type == JgsType.Cell ? other.AsCell : other.BoxedElements();
            theirs = [.. names.Select(Code)];
            (rows, columns) = (JgsMatrix.RowCount(other), JgsMatrix.ColCount(other));
        }
        else if (other.Type is JgsType.Number or JgsType.Bool)
        {
            (theirs, rows, columns) = ([other.AsNumber], 1, 1);
        }
        else if (other.Type == JgsType.Array && !other.IsCategorical)
        {
            theirs = [.. Enumerable.Range(0, other.ArrayLength).Select(i => other.ElementAt(i).AsNumber)];
            (rows, columns) = (JgsMatrix.RowCount(other), JgsMatrix.ColCount(other));
        }
        else
        {
            return JgsValue.Bool(false);
        }

        int count = Math.Max(States.Length, theirs.Length);
        if (!(States.Length == 1 || theirs.Length == 1 || (Rows == rows && Columns == columns)))
        {
            return null;
        }

        var same = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            double mine = States[States.Length == 1 ? 0 : i] ? 1 : 0;
            same[i] = JgsValue.Bool(mine == theirs[theirs.Length == 1 ? 0 : i]);
        }

        if (count == 1)
        {
            return same[0];
        }

        (int outRows, int outColumns) = States.Length == 1 && theirs.Length != 1 ? (rows, columns) : (Rows, Columns);
        JgsValue mask = JgsValue.Array(same);
        mask.Reshape(outRows, outColumns);
        return mask;
    }
}
