using JGraph.Core.Drawing;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M79's second block: a pie answered as the patch MATLAB draws it with.
/// <para>
/// MATLAB has no pie object — <c>pie</c> makes one patch per wedge and a text beside it — so the
/// property table has always scored a pie against a patch, and a pie answered 25 of those 56 names.
/// The 31 missing ones were the mesh, its colours and its lighting, and until M79 there was nothing
/// under them to answer with: a pie drew its own polygons. Now the wedges <em>are</em> a patch, so
/// the whole block is served over it and every one of those names acts on the drawing.
/// </para>
/// <para>
/// The geometry is the exception, and deliberately: a pie's shape is worked out from its values, so
/// <c>Faces</c>, <c>Vertices</c> and the three coordinate arrays are read and refuse a write, with
/// the message naming the properties that <em>do</em> change the shape.
/// </para>
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>
    /// The patch behind an object: a patch is one, and a pie is drawn as one. Every patch property
    /// goes through here, which is the whole of what it takes to serve both kinds from one block.
    /// </summary>
    private static PatchPlot PatchOf(JgsHandleEntry entry) =>
        entry.Target is PiePlot pie ? pie.Patch : (PatchPlot)entry.Target;

    /// <summary>
    /// A constant line's label style, as it is drawn rather than as it is stored: the slot is empty
    /// until somebody writes to it, and what is drawn then is ten-point text in the line's own colour.
    /// Reading the drawn style is what lets <c>FontSize</c> answer before it is set.
    /// </summary>
    private static TextStyle LabelStyleOf(ConstantLinePlot line) =>
        line.LabelStyle ?? new TextStyle(
            line.Color ?? JgsBuiltins.PaletteColorFor(line), 10);

    private static void AddPieShapeBlock(IDictionary<string, GraphicsProperty> table)
    {
        // The mesh, read as a patch's and refused as a pie's: these five describe the wedges rather
        // than decide them, and the four properties named in the refusal are what decides them.
        Put(table, "XData", entry => Row([.. PatchOf(entry).X]), RefuseShape("XData"));
        Put(table, "YData", entry => Row([.. PatchOf(entry).Y]), RefuseShape("YData"));
        Put(table, "ZData", entry => Row([.. PatchOf(entry).Z]), RefuseShape("ZData"));
        Put(table, "Faces", entry => FaceTable(PatchOf(entry)), RefuseShape("Faces"));
        Put(table, "Vertices", entry => VertexTable(PatchOf(entry)), RefuseShape("Vertices"));

        // A pie's faces are coloured one per wedge unless a script says otherwise, and MATLAB's word
        // for that is 'flat' — the same reading a surface's FaceColor takes here. So the three
        // answers are the word for no fill, the word for a colour per face, and a colour.
        Put(table, "FaceColor",
            entry =>
            {
                PatchPlot wedges = PatchOf(entry);
                return !wedges.FaceVisible ? JgsValue.Str("none")
                    : wedges.FaceColor is { } chosen ? ColorRow(chosen)
                    : JgsValue.Str("flat");
            },
            (entry, value, line, col) =>
            {
                PatchPlot wedges = PatchOf(entry);
                if (value.Type == JgsType.String)
                {
                    string word = value.AsString;
                    if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        wedges.FaceVisible = false;
                        return;
                    }

                    if (word.Equals("flat", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("interp", StringComparison.OrdinalIgnoreCase))
                    {
                        wedges.FaceColor = null;
                        wedges.FaceVisible = true;
                        return;
                    }
                }

                wedges.FaceColor = JgsBuiltins.OptionColor(value, line, col, "pie");
                wedges.FaceVisible = true;
            });

        AddAlphaNumber(table, "EdgeAlpha",
            entry => PatchOf(entry).EdgeAlpha,
            (entry, alpha) => PatchOf(entry).EdgeAlpha = alpha);

        AddWordProperty(table, "FaceLighting",
            entry => LightingWord(PatchOf(entry).FaceLighting),
            (entry, word, line, col) => PatchOf(entry).FaceLighting =
                ToLighting("FaceLighting", word, line, col));

        AddMaterialNumber(table, "AmbientStrength",
            entry => PatchOf(entry).AmbientStrength,
            (entry, value) => PatchOf(entry).AmbientStrength = value);
        AddMaterialNumber(table, "DiffuseStrength",
            entry => PatchOf(entry).DiffuseStrength,
            (entry, value) => PatchOf(entry).DiffuseStrength = value);
        AddMaterialNumber(table, "SpecularStrength",
            entry => PatchOf(entry).SpecularStrength,
            (entry, value) => PatchOf(entry).SpecularStrength = value);
        AddMaterialNumber(table, "SpecularColorReflectance",
            entry => PatchOf(entry).SpecularColorReflectance,
            (entry, value) => PatchOf(entry).SpecularColorReflectance = value);

        // The exponent is the one of the five that is not a fraction: it is how tight the highlight
        // is, and MATLAB's own default is ten.
        Put(table, "SpecularExponent",
            entry => JgsValue.Number(PatchOf(entry).SpecularExponent),
            (entry, value, line, col) =>
            {
                double given = Numbers("SpecularExponent", value, 1, line, col)[0];
                if (given <= 0 || !double.IsFinite(given))
                {
                    throw new JgsRuntimeException(line, col,
                        $"SpecularExponent is a positive number, but got {given:G6}.");
                }

                PatchOf(entry).SpecularExponent = given;
            });

        AddPatchBlock(table);
    }

    /// <summary>
    /// A number in [0, 1] that says how much of one kind of light a surface returns. Four of MATLAB's
    /// five material properties are this shape, and out of range is a mistake rather than a clamp.
    /// </summary>
    private static void AddMaterialNumber(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, double> read,
        Action<JgsHandleEntry, double> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => JgsValue.Number(read(entry)),
            (entry, value, line, col) =>
            {
                double given = Numbers(spelling, value, 1, line, col)[0];
                if (given is < 0 or > 1 || !double.IsFinite(given))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling} is a number from 0 through 1, but got {given:G6}.");
                }

                write(entry, given);
            });
    }

    /// <summary>An opacity kept as a number of its own rather than inside a colour's alpha.</summary>
    private static void AddAlphaNumber(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, double> read,
        Action<JgsHandleEntry, double> write) =>
        AddMaterialNumber(table, name, read, write);

    /// <summary>
    /// A pie's mesh is worked out from its values, so writing one is refused by name — and the
    /// refusal says which properties do move it, since that is the question behind the write.
    /// </summary>
    private static Action<JgsHandleEntry, JgsValue, int, int> RefuseShape(string name)
    {
        string spelling = name;
        return (entry, _, line, col) =>
        {
            if (entry.Target is not PiePlot)
            {
                throw new JgsRuntimeException(line, col, $"{spelling} cannot be written on this object.");
            }

            throw new JgsRuntimeException(line, col,
                $"{spelling} describes the wedges a pie worked out from its values rather than deciding "
                + "them — set Values, Explode, StartAngle or Clockwise instead.");
        };
    }
}
