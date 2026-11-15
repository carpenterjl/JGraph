using System.Runtime.CompilerServices;
using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The leftovers of ADR 0372 (open item 343, ADR 0376): a marker face R2025b reads as <c>'auto'</c>,
/// the marker words <c>'flat'</c> and <c>'auto'</c> on a surface and a patch, and the tiled layout as a
/// node of the figure tree.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly object TreeLeftoverMark = new();

    // --- the tiled layout as a node of the tree ---------------------------------------------------

    /// <summary>The tiled layouts a MATLAB-dialect script made, which a sweep run outside one keeps alive.</summary>
    private static readonly ConditionalWeakTable<TiledLayoutModel, object> MatlabLayouts = new();

    /// <summary>Marks a layout <c>tiledlayout</c> or <c>nexttile</c> made in a MATLAB-dialect script.</summary>
    internal static void LayoutMade(TiledLayoutModel layout)
    {
        if (JgsRunningDialect.ThreadIsMatlab)
        {
            MatlabLayouts.AddOrUpdate(layout, TreeLeftoverMark);
        }
    }

    private static bool MadeInMatlab(TiledLayoutModel layout) => MatlabLayouts.TryGetValue(layout, out _);

    /// <summary>
    /// The tiled layout an axes is a tile of, in a MATLAB-dialect script: R2025b's tile is the layout's
    /// child, and its legend and colour bar with it (open item 343, ADR 0376; probe p7fb). Null for any
    /// other axes and outside the MATLAB dialect, where a tile is its figure's child as it always was.
    /// </summary>
    internal static TiledLayoutModel? LayoutHolding(AxesModel axes) =>
        JgsRunningDialect.ThreadIsMatlab && axes.Container is null
        && axes.Parent is FigureModel { TiledLayout: { } layout } && layout.Tiles.Contains(axes)
            ? layout
            : null;

    /// <summary>
    /// A figure's axes, each followed by its shown furniture, with its tiled layout standing for its tiles
    /// in a MATLAB-dialect script, back first by when each was made (R2025b: an axes made after a layout
    /// stands in front of it, p7fd).
    /// </summary>
    private static void AddFigureAxes(List<GraphObject> children, FigureModel figure)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || figure.TiledLayout is not { } layout)
        {
            AddAxesAndFurniture(children, figure.TopLevelAxes());
            return;
        }

        var free = new List<AxesModel>();
        bool placed = false;
        foreach (AxesModel axes in figure.TopLevelAxes())
        {
            if (layout.Tiles.Contains(axes))
            {
                continue;
            }

            if (!placed && axes.CreationOrder > layout.CreationOrder)
            {
                AddAxesAndFurniture(children, free);
                free.Clear();
                children.Add(layout);
                placed = true;
            }

            free.Add(axes);
        }

        AddAxesAndFurniture(children, free);
        if (!placed)
        {
            children.Add(layout);
        }
    }

    /// <summary>
    /// A tiled layout's children, back first: its tiles and their shown legends, colour bars and bubble
    /// legends, by when each tile was made and each piece of furniture last shown (R2025b's
    /// <c>t.Children</c>, newest first, p7fd).
    /// </summary>
    private static IEnumerable<GraphObject> LayoutChildren(TiledLayoutModel layout)
    {
        var held = new List<(long At, GraphObject Child)>();
        foreach (AxesModel tile in layout.Tiles)
        {
            if (tile.Parent is not FigureModel figure || !ReferenceEquals(figure.TiledLayout, layout) || tile.Container is not null)
            {
                continue;
            }

            held.Add((tile.CreationOrder, tile));
            foreach (GraphObject furniture in FurnitureOf(tile))
            {
                held.Add((System.Math.Max(tile.ShownOrder(furniture), tile.CreationOrder), furniture));
            }
        }

        return held.OrderBy(static h => h.At).Select(static h => h.Child);
    }

    /// <summary>
    /// The stems and lines whose marker face is R2025b's <c>'auto'</c>: a stem made <c>'filled'</c>, or a
    /// face written <c>'auto'</c> in a MATLAB-dialect script. The model holds the colour it is drawn in.
    /// </summary>
    private static readonly ConditionalWeakTable<GraphObject, object> MarkerFaceAuto = new();

    /// <summary>Marks a stem's face as R2025b's <c>'auto'</c> (stem and stem3 <c>'filled'</c> in the MATLAB dialect).</summary>
    internal static void MarkMarkerFaceAuto(GraphObject plot)
    {
        if (JgsRunningDialect.ThreadIsMatlab)
        {
            MarkerFaceAuto.AddOrUpdate(plot, TreeLeftoverMark);
        }
    }

    /// <summary>
    /// Whether a default for <paramref name="property"/> of the object's class is held above it, which
    /// <see cref="ApplyDefaults"/> has then written: a maker's own factory value must not overwrite it.
    /// </summary>
    internal static bool HoldsDefault(GraphObject child, string property)
    {
        if (!s_anyDefaults || ClassOfObject(child) is not { } cls)
        {
            return false;
        }

        string key = Key(cls.Word, property);
        for (GraphObject? holder = HolderAbove(child); holder is not null; holder = HolderAbove(holder))
        {
            if (StoreOf(holder, create: false) is { } store && store.ContainsKey(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The marker words <c>'flat'</c> and <c>'auto'</c> a MATLAB-dialect script wrote on a surface's or a
    /// patch's marker face, and <c>'flat'</c> on its marker edge, read back as written (open item 343,
    /// ADR 0376; probe p7fa). The model's slot holds null, drawn as an unfilled marker and an edge that
    /// follows the object's own edge.
    /// </summary>
    private static readonly ConditionalWeakTable<GraphObject, string> MarkerFaceWord = new();

    /// <inheritdoc cref="MarkerFaceWord" />
    private static readonly ConditionalWeakTable<GraphObject, string> MarkerEdgeWord = new();

    private static readonly string[] ColourNames = ["red", "green", "blue", "cyan", "magenta", "yellow", "black", "white"];

    /// <summary>
    /// R2025b's refusal of a word that names no colour on a marker colour property, in its words: the
    /// valid names are the eight colours, then <paramref name="words"/> (<c>'flat', 'auto', and 'none'</c>
    /// on a surface or a patch, <c>'auto', and 'none'</c> on a line). Null when the value is not text or
    /// names a colour, a hex code or one of the words.
    /// </summary>
    private static JgsRuntimeException? MarkerColourRefusal(
        JgsHandleEntry entry, string property, JgsValue value, string[] words, int line, int col)
    {
        if (!JgsBuiltins.IsTextScalar(value))
        {
            return null;
        }

        string word = JgsBuiltins.TextOf(value).Trim();
        if (word.Length > 0
            && (word.StartsWith('#')
                || (word.Length == 1 && "rgbcmykw".Contains(char.ToLowerInvariant(word[0])))
                || ColourNames.Any(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                || words.Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        string listed = string.Join(", ", ColourNames.Concat(words[..^1]).Select(static w => $"'{w}'")) + $", and '{words[^1]}'";
        return new JgsRuntimeException(line, col, "MATLAB:datatypes:RGBAColor:ParseError",
            $"Error setting property '{property}' of class '{JgsGraphicsCallbackValues.ClassWord(entry.Target)}':\n"
            + $"Invalid color name or hexadecimal color code. Valid names include: {listed}. "
            + "Valid hexadecimal color codes consist of '#' followed by three or six hexadecimal digits.");
    }

    private static readonly string[] SurfaceMarkerWords = ["flat", "auto", "none"];

    private static readonly string[] LineMarkerWords = ["auto", "none"];

    /// <summary>The word a surface's or a patch's marker slot was written, when it was one (ADR 0376).</summary>
    private static string? MarkerWordOf(ConditionalWeakTable<GraphObject, string> words, GraphObject target) =>
        JgsRunningDialect.ThreadIsMatlab && words.TryGetValue(target, out string? word) ? word : null;

    /// <summary>
    /// A surface's or a patch's marker slot written in the MATLAB dialect: <c>'flat'</c> (and on the face
    /// <c>'auto'</c>) is remembered and answers true, with null for the model; anything else forgets the
    /// word and answers false.
    /// </summary>
    private static bool WritesMarkerWord(ConditionalWeakTable<GraphObject, string> words, GraphObject target, JgsValue value, bool face)
    {
        if (JgsRunningDialect.ThreadIsMatlab && JgsBuiltins.IsTextScalar(value))
        {
            string word = JgsBuiltins.TextOf(value).Trim().ToLowerInvariant();
            if (word.Length > 0 && ("flat".StartsWith(word, StringComparison.Ordinal) || (face && "auto".StartsWith(word, StringComparison.Ordinal))))
            {
                words.AddOrUpdate(target, "flat".StartsWith(word, StringComparison.Ordinal) ? "flat" : "auto");
                return true;
            }
        }

        words.Remove(target);
        return false;
    }

    /// <summary>
    /// A line's <c>XVariable</c> and <c>YVariable</c> in the MATLAB dialect (its <c>SourceTable</c> is
    /// <see cref="AddSourceTable"/>'s, which reads the same record): the
    /// table <c>plot(tbl, ...)</c> drew it from and the variable behind each channel, a number reading
    /// back as the number; writing a channel redraws the line (open item 343, ADR 0376; probe p7fa). A
    /// line drawn from arrays reads them empty.
    /// </summary>
    private static void AddLineSourceBlock(IDictionary<string, GraphicsProperty> table)
    {
        static bool Matlab(GraphObject _) => JgsRunningDialect.ThreadIsMatlab;
        foreach (string channel in (string[])["XVariable", "YVariable"])
        {
            string spelling = channel;
            bool isX = spelling == "XVariable";
            table[spelling] = new GraphicsProperty(spelling,
                entry => entry.ScatterSource is { } source
                    ? source.Given is { } given && given.TryGetValue(spelling, out JgsValue? kept) ? kept
                        : JgsValue.Str(isX ? source.XVariable : source.YVariable)
                    : JgsValue.Str(string.Empty),
                (entry, value, line, col) =>
                {
                    ScatterSource source = RequireScatterSource(entry, spelling, line, col);
                    JGraph.Data.Table held = source.Table.AsTable;
                    string name;
                    if (JgsBuiltins.NumberedVariable(held, value) is { } number)
                    {
                        name = held.ColumnNames[number - 1];
                        source.Remember(spelling, JgsValue.Number(number));
                    }
                    else
                    {
                        name = JgsBuiltins.StrOf(spelling, value, line, col);
                        source.Remember(spelling, null);
                    }

                    if (isX)
                    {
                        source.XVariable = name;
                    }
                    else
                    {
                        source.YVariable = name;
                    }

                    JgsBuiltins.ReplotFromSource(entry, line, col);
                })
            { OnlyWhen = Matlab };
        }
    }

    private static bool ReadsMarkerFaceAuto(GraphObject plot) =>
        JgsRunningDialect.ThreadIsMatlab && MarkerFaceAuto.TryGetValue(plot, out _);

    /// <summary>
    /// A marker face written: <c>'auto'</c> in the MATLAB dialect marks the object and answers the colour
    /// it is drawn in (<paramref name="drawn"/>); anything else forgets the mark and answers null for the
    /// caller to write as it always has.
    /// </summary>
    private static Color? WritesMarkerFaceAuto(GraphObject plot, JgsValue value, Color drawn)
    {
        if (JgsRunningDialect.ThreadIsMatlab && IsAutoWord(value))
        {
            MarkerFaceAuto.AddOrUpdate(plot, TreeLeftoverMark);
            return drawn;
        }

        MarkerFaceAuto.Remove(plot);
        return null;
    }
}
