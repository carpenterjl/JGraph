using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Rendering;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The M78 wave: the furniture a chart is read and legended by. The polar rulers answer to the same
/// letter-shaped block their Cartesian counterparts do, the legend and the colorbar gain the boxes,
/// inks and fonts MATLAB documents for them, and a text label learns the words for its own turn and
/// its own edge.
/// <para>
/// Nothing here is a second copy of anything: the R and θ blocks are the very method that serves X,
/// Y and Z, called twice more, and the container properties of a chart that is a plot here answer for
/// the axes it is drawn on — the shape the heatmap's Title has answered in since it was written.
/// </para>
/// </summary>
internal static partial class JgsGraphicsProperties
{
    // --- The polar rulers -----------------------------------------------------------------------

    /// <summary>
    /// The r and θ rulers' inks, modes and minor ticks. MATLAB gives a polar axes exactly the block it
    /// gives a Cartesian one with the letters changed, so this is <see cref="AddRulerWave"/> called
    /// twice more rather than a polar copy of it — the grid switches are the only part a circle needs
    /// its own state for, because rings and spokes are not x and y lines under other names.
    /// </summary>
    private static void AddPolarRulerBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddRulerWave(table, "R", static axes => axes.RAxis);
        AddRulerWave(table, "Theta", static axes => axes.ThetaAxis);

        Put(table, "RGrid",
            entry => OnOff(Axes(entry).Grid.ShowMajorR),
            (entry, value, line, col) => Axes(entry).Grid.ShowMajorR = ToOnOff("RGrid", value, line, col));
        Put(table, "ThetaGrid",
            entry => OnOff(Axes(entry).Grid.ShowMajorTheta),
            (entry, value, line, col) => Axes(entry).Grid.ShowMajorTheta =
                ToOnOff("ThetaGrid", value, line, col));
        Put(table, "RMinorGrid",
            entry => OnOff(Axes(entry).Grid.ShowMinorR),
            (entry, value, line, col) => Axes(entry).Grid.ShowMinorR =
                ToOnOff("RMinorGrid", value, line, col));
        Put(table, "ThetaMinorGrid",
            entry => OnOff(Axes(entry).Grid.ShowMinorTheta),
            (entry, value, line, col) => Axes(entry).Grid.ShowMinorTheta =
                ToOnOff("ThetaMinorGrid", value, line, col));

        // The angle the r labels are written along carries a real flag rather than a nullable: 80° is
        // both the automatic answer and an angle a script may ask for, so the two cannot be told apart
        // by the value alone. Releasing it puts the labels back on that default spoke.
        AddNullableMode(table, "RAxisLocationMode",
            entry => Axes(entry).RAxisLocationManual,
            entry => Axes(entry).RAxisLocationManual = true,
            entry =>
            {
                AxesModel axes = Axes(entry);
                axes.RAxisLocation = 80;
                axes.RAxisLocationManual = false;
            });
    }

    // --- A run of text, wherever it is written --------------------------------------------------

    /// <summary>
    /// The six words every piece of MATLAB text answers to, over whatever <see cref="TextStyle"/> the
    /// object keeps. A style is immutable, so each write reads, replaces and stores — which is why
    /// this is one method taking a pair of accessors rather than six copies per object.
    /// </summary>
    /// <remarks>
    /// <c>FontUnits</c> answers 'points' and refuses anything else: nothing in this build measures a
    /// glyph in pixels or inches, and a property that accepted the word without honouring it would
    /// report a size that is not the size drawn.
    /// </remarks>
    private static void AddTextStyleBlock(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, TextStyle> read,
        Action<JgsHandleEntry, TextStyle> write,
        string? colorName = null)
    {
        Put(table, "FontName",
            entry => JgsValue.Str(read(entry).FontFamily),
            (entry, value, line, col) => write(entry,
                read(entry).WithFamily(JgsBuiltins.StrOf("FontName", value, line, col))));

        Put(table, "FontSize",
            entry => JgsValue.Number(read(entry).FontSize),
            (entry, value, line, col) =>
            {
                double size = Numbers("FontSize", value, 1, line, col)[0];
                if (size <= 0 || !double.IsFinite(size))
                {
                    throw new JgsRuntimeException(line, col, $"FontSize is a positive number, but got {size:G6}.");
                }

                write(entry, read(entry).WithSize(size));
            });

        AddWordProperty(table, "FontWeight",
            entry => read(entry).Bold ? "bold" : "normal",
            (entry, word, line, col) => write(entry, read(entry).WithBold(word switch
            {
                "bold" => true,
                "normal" => false,
                _ => throw new JgsRuntimeException(
                    line, col, $"FontWeight is 'normal' or 'bold', but got '{word}'."),
            })));

        AddWordProperty(table, "FontAngle",
            entry => read(entry).Italic ? "italic" : "normal",
            (entry, word, line, col) => write(entry, read(entry).WithItalic(word switch
            {
                "italic" => true,
                "normal" => false,
                _ => throw new JgsRuntimeException(
                    line, col, $"FontAngle is 'normal' or 'italic', but got '{word}'."),
            })));

        AddWordProperty(table, "FontSmoothing",
            entry => OnOffWord(read(entry).Antialias),
            (entry, word, line, col) => write(entry,
                read(entry).WithAntialias(ToOnOff("FontSmoothing", JgsValue.Str(word), line, col))));

        AddWordProperty(table, "FontUnits",
            static _ => "points",
            static (_, word, line, col) =>
            {
                if (!word.Equals("points", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Font sizes are in points here, and '{word}' is not a unit this build measures in.");
                }
            });

        if (colorName is { } ink)
        {
            Put(table, ink,
                entry => ColorRow(read(entry).Color),
                (entry, value, line, col) => write(entry,
                    read(entry).WithColor(JgsBuiltins.OptionColor(value, line, col, ink))));
        }

        Put(table, "Interpreter",
            entry => JgsValue.Str(JgsBuiltins.InterpreterWord(read(entry).Interpreter)),
            (entry, value, line, col) => write(entry, read(entry).WithInterpreter(
                JgsBuiltins.ParseInterpreter(
                    "text", JgsBuiltins.StrOf("Interpreter", value, line, col), line, col))));
    }

    private static string OnOffWord(bool on) => on ? "on" : "off";

    // --- The legend box -------------------------------------------------------------------------

    /// <summary>
    /// The legend's box, ink and font, plus the two properties that decide how its rows are dealt out.
    /// <c>Position</c> is the interesting one: this model has always answered it with the name of a
    /// corner, and MATLAB answers it with a rectangle — so the rectangle wins here and the corner
    /// keeps the name MATLAB gives it, which is <c>Location</c>.
    /// </summary>
    private static void AddLegendBlock(IDictionary<string, GraphicsProperty> table)
    {
        static LegendModel Box(JgsHandleEntry entry) => (LegendModel)entry.Target;

        AddTextStyleBlock(table,
            entry => Box(entry).TextStyle,
            (entry, style) => Box(entry).TextStyle = style,
            colorName: "TextColor");

        Put(table, "Color",
            entry => ColorRow(Box(entry).Background),
            (entry, value, line, col) => Box(entry).Background =
                JgsBuiltins.OptionColor(value, line, col, "legend"));

        Put(table, "EdgeColor",
            entry => ColorRow(Box(entry).BorderColor),
            (entry, value, line, col) => Box(entry).BorderColor =
                JgsBuiltins.OptionColor(value, line, col, "legend"));

        Put(table, "Box",
            entry => OnOff(Box(entry).ShowBorder),
            (entry, value, line, col) => Box(entry).ShowBorder = ToOnOff("Box", value, line, col));

        Put(table, "LineWidth",
            entry => JgsValue.Number(Box(entry).BorderWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width < 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth cannot be negative, but got {width:G6}.");
                }

                Box(entry).BorderWidth = width;
            });

        Put(table, "AutoUpdate",
            entry => OnOff(Box(entry).AutoUpdate),
            (entry, value, line, col) => Box(entry).AutoUpdate = ToOnOff("AutoUpdate", value, line, col));

        AddWordProperty(table, "Orientation",
            entry => Box(entry).Orientation == LegendOrientation.Horizontal ? "horizontal" : "vertical",
            (entry, word, line, col) => Box(entry).Orientation = word switch
            {
                "vertical" => LegendOrientation.Vertical,
                "horizontal" => LegendOrientation.Horizontal,
                _ => throw new JgsRuntimeException(
                    line, col, $"Orientation is 'vertical' or 'horizontal', but got '{word}'."),
            });

        // Answered as the number of columns actually used, never as an unset slot: a script asking a
        // horizontal legend how wide it is should be told, not handed the empty the model stores.
        // A chosen number answers as itself rather than clamped to the row count, because the rows
        // are settled at draw time and a legend read before its first frame has none yet.
        Put(table, "NumColumns",
            entry => JgsValue.Number(
                Box(entry).Columns ?? Box(entry).ResolveColumns(Box(entry).Entries.Count)),
            (entry, value, line, col) =>
            {
                double columns = Numbers("NumColumns", value, 1, line, col)[0];
                if (columns < 1 || !double.IsFinite(columns))
                {
                    throw new JgsRuntimeException(
                        line, col, $"NumColumns is a positive whole number, but got {columns:G6}.");
                }

                Box(entry).Columns = (int)System.Math.Round(columns);
            });

        AddNullableMode(table, "NumColumnsMode",
            entry => Box(entry).Columns is not null,
            entry => Box(entry).Columns ??= Box(entry).ResolveColumns(Box(entry).Entries.Count),
            entry => Box(entry).Columns = null);

        AddFurniturePosition(table,
            entry => Box(entry).LastBox,
            entry => Box(entry).FigureBox,
            (entry, box) =>
            {
                LegendModel legend = Box(entry);
                legend.FigureBox = box;
                legend.Position = LegendPosition.Custom;
            },
            entry => Box(entry).Parent as AxesModel);
    }

    // --- The surface ----------------------------------------------------------------------------

    /// <summary>
    /// A surface's mesh, markers, colour mapping and lighting words. The normals are the part worth
    /// naming: MATLAB answers them as an m-by-n-by-3 array, and this build computes them from the
    /// grid rather than storing them, so what a script reads is what the lighting used.
    /// </summary>
    private static void AddSurfaceBlock(IDictionary<string, GraphicsProperty> table)
    {
        static SurfacePlot Sheet(JgsHandleEntry entry) => (SurfacePlot)entry.Target;

        Put(table, "CData",
            entry => Grid(Sheet(entry).CData ?? Sheet(entry).Z),
            (entry, value, line, col) =>
            {
                var sheet = Sheet(entry);
                double[,] given = JgsBuiltins.HeatmapGrid(value, line, col);
                if (given.GetLength(0) != sheet.Z.GetLength(0) || given.GetLength(1) != sheet.Z.GetLength(1))
                {
                    throw new JgsRuntimeException(line, col,
                        $"CData needs one value per grid vertex: "
                        + $"{given.GetLength(0)}-by-{given.GetLength(1)} given, "
                        + $"{sheet.Z.GetLength(0)}-by-{sheet.Z.GetLength(1)} wanted.");
                }

                sheet.CData = given;
            });

        AddNullableMode(table, "CDataMode",
            entry => Sheet(entry).CData is not null,
            entry => Sheet(entry).CData ??= (double[,])Sheet(entry).Z.Clone(),
            entry => Sheet(entry).CData = null);

        AddWordProperty(table, "CDataMapping",
            entry => Sheet(entry).CDataMapping == ColorMapping.Direct ? "direct" : "scaled",
            (entry, word, line, col) => Sheet(entry).CDataMapping = word switch
            {
                "scaled" => ColorMapping.Scaled,
                "direct" => ColorMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"CDataMapping is 'scaled' or 'direct', but got '{word}'."),
            });

        AddWordProperty(table, "AlphaDataMapping",
            entry => Sheet(entry).AlphaDataMapping.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Sheet(entry).AlphaDataMapping = word switch
            {
                "none" => AlphaMapping.None,
                "scaled" => AlphaMapping.Scaled,
                "direct" => AlphaMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"AlphaDataMapping is 'none', 'scaled' or 'direct', but got '{word}'."),
            });

        AddWordProperty(table, "MeshStyle",
            entry => Sheet(entry).MeshStyle.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Sheet(entry).MeshStyle = word switch
            {
                "both" => SurfaceMeshStyle.Both,
                "row" => SurfaceMeshStyle.Row,
                "column" => SurfaceMeshStyle.Column,
                _ => throw new JgsRuntimeException(
                    line, col, $"MeshStyle is 'both', 'row' or 'column', but got '{word}'."),
            });

        Put(table, "LineStyle",
            entry => JgsValue.Str(DashWord(Sheet(entry).EdgeDash)),
            (entry, value, line, col) => Sheet(entry).EdgeDash =
                ToDash("LineStyle", JgsBuiltins.StrOf("LineStyle", value, line, col), line, col));

        Put(table, "LineWidth",
            entry => JgsValue.Number(Sheet(entry).EdgeWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width <= 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth is a positive number, but got {width:G6}.");
                }

                Sheet(entry).EdgeWidth = width;
            });

        AddMarkerBlock(table,
            entry => Sheet(entry).Marker,
            (entry, marker) => Sheet(entry).Marker = marker,
            entry => Sheet(entry).MarkerSize,
            (entry, size) => Sheet(entry).MarkerSize = size,
            entry => Sheet(entry).MarkerEdge,
            (entry, ink) => Sheet(entry).MarkerEdge = ink,
            entry => Sheet(entry).MarkerFill,
            (entry, ink) => Sheet(entry).MarkerFill = ink);

        AddWordProperty(table, "EdgeLighting",
            entry => LightingWord(Sheet(entry).EdgeLighting),
            (entry, word, line, col) => Sheet(entry).EdgeLighting = ToLighting("EdgeLighting", word, line, col));

        AddWordProperty(table, "BackFaceLighting",
            entry => Sheet(entry).BackFaceLighting switch
            {
                Core.Drawing.BackFaceLighting.Unlit => "unlit",
                Core.Drawing.BackFaceLighting.Lit => "lit",
                _ => "reverselit",
            },
            (entry, word, line, col) => Sheet(entry).BackFaceLighting = word switch
            {
                "unlit" => Core.Drawing.BackFaceLighting.Unlit,
                "lit" => Core.Drawing.BackFaceLighting.Lit,
                "reverselit" => Core.Drawing.BackFaceLighting.ReverseLit,
                _ => throw new JgsRuntimeException(line, col,
                    $"BackFaceLighting is 'unlit', 'lit' or 'reverselit', but got '{word}'."),
            });

        Put(table, "AlignVertexCenters",
            entry => OnOff(Sheet(entry).AlignVertexCenters),
            (entry, value, line, col) => Sheet(entry).AlignVertexCenters =
                ToOnOff("AlignVertexCenters", value, line, col));

        // The two normal fields are worked out from the grid every time they are asked for, so they
        // agree with the drawing by construction. Writing one is refused rather than stored: a normal
        // this build did not compute is a normal it would not use, and silently keeping it would be
        // the property answering something the lighting never sees.
        Put(table, "FaceNormals", entry => Normals(Sheet(entry), perVertex: false), RefuseNormals("FaceNormals"));
        Put(table, "VertexNormals", entry => Normals(Sheet(entry), perVertex: true), RefuseNormals("VertexNormals"));

        // Computed, never chosen — so the mode is 'auto' and says so if a script tries to freeze it.
        AddWordProperty(table, "FaceNormalsMode", static _ => "auto", RefuseManual("FaceNormalsMode"));
        AddWordProperty(table, "VertexNormalsMode", static _ => "auto", RefuseManual("VertexNormalsMode"));

        AddNullableMode(table, "XDataMode",
            entry => !Sheet(entry).XImplied,
            entry => Sheet(entry).XImplied = false,
            entry =>
            {
                var sheet = Sheet(entry);
                sheet.X = Counted(sheet.Z.GetLength(1));
                sheet.XImplied = true;
            });

        AddNullableMode(table, "YDataMode",
            entry => !Sheet(entry).YImplied,
            entry => Sheet(entry).YImplied = false,
            entry =>
            {
                var sheet = Sheet(entry);
                sheet.Y = Counted(sheet.Z.GetLength(0));
                sheet.YImplied = true;
            });
    }

    /// <summary>
    /// Replaces one of a rectilinear surface's two position vectors. The length has to match the grid
    /// it indexes — a surface is a sheet over the positions it was given, and a vector of another
    /// length describes a different sheet — and giving one explicitly is what takes its mode off auto.
    /// </summary>
    private static void WriteSurfaceRuler(JgsHandleEntry entry, JgsValue value, int line, int col, bool alongX)
    {
        var sheet = (SurfacePlot)entry.Target;
        string what = alongX ? "XData" : "YData";
        if (sheet.IsParametric)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} on a parametric surface is a matrix of positions that only means anything "
                + "beside its partner — draw the surface again with the grids you want.");
        }

        double[] given = JgsBuiltins.ToDoubles(what, value, line, col);
        int wanted = sheet.Z.GetLength(alongX ? 1 : 0);
        if (given.Length != wanted)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} needs one value per grid {(alongX ? "column" : "row")}: "
                + $"{given.Length} given, {wanted} wanted.");
        }

        if (alongX)
        {
            sheet.X = given;
            sheet.XImplied = false;
        }
        else
        {
            sheet.Y = given;
            sheet.YImplied = false;
        }
    }

    // --- The arrow field ------------------------------------------------------------------------

    /// <summary>
    /// A quiver's line and marker words, and the four modes over them. The dash and the marker carry
    /// real flags rather than nullables, for the reason M77 found on a line: their automatic values —
    /// solid and none — are also values a script may choose, so the value cannot tell the two apart.
    /// </summary>
    private static void AddQuiverBlock(IDictionary<string, GraphicsProperty> table)
    {
        static QuiverPlot Field(JgsHandleEntry entry) => (QuiverPlot)entry.Target;

        Put(table, "LineStyle",
            entry => JgsValue.Str(DashWord(Field(entry).LineDash)),
            (entry, value, line, col) => Field(entry).LineDash =
                ToDash("LineStyle", JgsBuiltins.StrOf("LineStyle", value, line, col), line, col));

        AddMarkerBlock(table,
            entry => Field(entry).Marker,
            (entry, marker) => Field(entry).Marker = marker,
            entry => Field(entry).MarkerSize,
            (entry, size) => Field(entry).MarkerSize = size,
            entry => Field(entry).MarkerEdge,
            (entry, ink) => Field(entry).MarkerEdge = ink,
            entry => Field(entry).MarkerFill,
            (entry, ink) => Field(entry).MarkerFill = ink);

        Put(table, "AlignVertexCenters",
            entry => OnOff(Field(entry).AlignVertexCenters),
            (entry, value, line, col) => Field(entry).AlignVertexCenters =
                ToOnOff("AlignVertexCenters", value, line, col));

        AddColorMode(table,
            entry => Field(entry).Color,
            (entry, color) => Field(entry).Color = color);

        AddNullableMode(table, "LineStyleMode",
            entry => Field(entry).LineStyleManual,
            entry => Field(entry).LineStyleManual = true,
            entry =>
            {
                QuiverPlot field = Field(entry);
                field.LineDash = DashStyle.Solid;
                field.LineStyleManual = false;
            });

        AddNullableMode(table, "MarkerMode",
            entry => Field(entry).MarkerManual,
            entry => Field(entry).MarkerManual = true,
            entry =>
            {
                QuiverPlot field = Field(entry);
                field.Marker = MarkerType.None;
                field.MarkerManual = false;
            });

        // An arrow field is given its tails or counts them out of the grid, exactly as a line is.
        AddNullableMode(table, "XDataMode",
            entry => !Field(entry).XImplied,
            entry => Field(entry).XImplied = false,
            entry => Field(entry).XImplied = true);
        AddNullableMode(table, "YDataMode",
            entry => !Field(entry).YImplied,
            entry => Field(entry).YImplied = false,
            entry => Field(entry).YImplied = true);
    }

    // --- The image ------------------------------------------------------------------------------

    /// <summary>
    /// An image's own four names. <c>XData</c> and <c>YData</c> are a pair rather than a grid: MATLAB
    /// gives an image the centres of its first and last cell in each direction, and this build keeps
    /// the same pair as the extent it draws into.
    /// </summary>
    private static void AddImageBlock(IDictionary<string, GraphicsProperty> table)
    {
        static ImagePlot Picture(JgsHandleEntry entry) => (ImagePlot)entry.Target;

        Put(table, "CData",
            entry => Grid(Picture(entry).Values),
            (entry, value, line, col) => Picture(entry).Values =
                JgsBuiltins.HeatmapGrid(value, line, col));

        AddWordProperty(table, "CDataMapping",
            entry => Picture(entry).CDataMapping == ColorMapping.Direct ? "direct" : "scaled",
            (entry, word, line, col) => Picture(entry).CDataMapping = word switch
            {
                "scaled" => ColorMapping.Scaled,
                "direct" => ColorMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"CDataMapping is 'scaled' or 'direct', but got '{word}'."),
            });

        AddWordProperty(table, "AlphaDataMapping",
            entry => Picture(entry).AlphaDataMapping.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Picture(entry).AlphaDataMapping = word switch
            {
                "none" => AlphaMapping.None,
                "scaled" => AlphaMapping.Scaled,
                "direct" => AlphaMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"AlphaDataMapping is 'none', 'scaled' or 'direct', but got '{word}'."),
            });

        AddWordProperty(table, "Interpolation",
            entry => Picture(entry).Interpolate ? "bilinear" : "nearest",
            (entry, word, line, col) => Picture(entry).Interpolate = word switch
            {
                "nearest" => false,
                "bilinear" => true,
                _ => throw new JgsRuntimeException(
                    line, col, $"Interpolation is 'nearest' or 'bilinear', but got '{word}'."),
            });

        AddImageExtent(table, "XData", horizontal: true);
        AddImageExtent(table, "YData", horizontal: false);
    }

    private static void AddImageExtent(
        IDictionary<string, GraphicsProperty> table, string name, bool horizontal)
    {
        string spelling = name;
        bool alongX = horizontal;

        Put(table, spelling,
            entry =>
            {
                var picture = (ImagePlot)entry.Target;
                DataRange extent = alongX ? picture.XExtent : picture.YExtent;
                return Row(extent.Min, extent.Max);
            },
            (entry, value, line, col) =>
            {
                var picture = (ImagePlot)entry.Target;
                double[] pair = Numbers(spelling, value, 2, line, col);
                var extent = new DataRange(
                    System.Math.Min(pair[0], pair[1]), System.Math.Max(pair[0], pair[1]));
                if (alongX)
                {
                    picture.XExtent = extent;
                }
                else
                {
                    picture.YExtent = extent;
                }
            });
    }

    // --- The patch ------------------------------------------------------------------------------

    /// <summary>
    /// A patch answers to almost exactly the surface's block, because it is the same thing said with
    /// an explicit vertex list: faces, edges, markers and lights. The two names that are its own are
    /// the vertex-indexed ones — MATLAB spells them <c>FaceVertexCData</c> and
    /// <c>FaceVertexAlphaData</c>, and they are the arrays the face list indexes into.
    /// </summary>
    private static void AddPatchBlock(IDictionary<string, GraphicsProperty> table)
    {
        static PatchPlot Shape(JgsHandleEntry entry) => (PatchPlot)entry.Target;

        Put(table, "CData",
            entry => Row([.. Shape(entry).ColorData ?? []]),
            (entry, value, line, col) =>
            {
                double[] given = JgsBuiltins.ToDoubles("CData", value, line, col);
                Shape(entry).ColorData = given.Length == 0 ? null : given;
            });

        // The same array under the name that says what indexes it. One property, two spellings, as
        // MATLAB has them — CData is the short way of naming a patch's colours and this is the long.
        Put(table, "FaceVertexCData",
            entry => Row([.. Shape(entry).ColorData ?? []]),
            (entry, value, line, col) =>
            {
                double[] given = JgsBuiltins.ToDoubles("FaceVertexCData", value, line, col);
                Shape(entry).ColorData = given.Length == 0 ? null : given;
            });

        Put(table, "FaceVertexAlphaData",
            entry => Row(Shape(entry).VertexAlpha ?? []),
            (entry, value, line, col) =>
            {
                var shape = Shape(entry);
                double[] given = JgsBuiltins.ToDoubles("FaceVertexAlphaData", value, line, col);
                if (given.Length != 0 && given.Length != shape.X.Count && given.Length != shape.Faces.Count)
                {
                    throw new JgsRuntimeException(line, col,
                        $"FaceVertexAlphaData is one value per vertex ({shape.X.Count}) or per face "
                        + $"({shape.Faces.Count}), but got {given.Length}.");
                }

                shape.VertexAlpha = given.Length == 0 ? null : given;
            });

        AddWordProperty(table, "CDataMapping",
            entry => Shape(entry).CDataMapping == ColorMapping.Direct ? "direct" : "scaled",
            (entry, word, line, col) => Shape(entry).CDataMapping = word switch
            {
                "scaled" => ColorMapping.Scaled,
                "direct" => ColorMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"CDataMapping is 'scaled' or 'direct', but got '{word}'."),
            });

        AddWordProperty(table, "AlphaDataMapping",
            entry => Shape(entry).AlphaDataMapping.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Shape(entry).AlphaDataMapping = word switch
            {
                "none" => AlphaMapping.None,
                "scaled" => AlphaMapping.Scaled,
                "direct" => AlphaMapping.Direct,
                _ => throw new JgsRuntimeException(
                    line, col, $"AlphaDataMapping is 'none', 'scaled' or 'direct', but got '{word}'."),
            });

        Put(table, "LineStyle",
            entry => JgsValue.Str(DashWord(Shape(entry).EdgeDash)),
            (entry, value, line, col) => Shape(entry).EdgeDash =
                ToDash("LineStyle", JgsBuiltins.StrOf("LineStyle", value, line, col), line, col));

        Put(table, "LineWidth",
            entry => JgsValue.Number(Shape(entry).EdgeWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width < 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth cannot be negative, but got {width:G6}.");
                }

                Shape(entry).EdgeWidth = width;
            });

        AddWordProperty(table, "LineJoin",
            entry => Shape(entry).LineJoin.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Shape(entry).LineJoin = word switch
            {
                "round" => LineJoin.Round,
                "miter" => LineJoin.Miter,
                "chamfer" or "bevel" => LineJoin.Bevel,
                _ => throw new JgsRuntimeException(
                    line, col, $"LineJoin is 'round', 'miter' or 'chamfer', but got '{word}'."),
            });

        AddMarkerBlock(table,
            entry => Shape(entry).Marker,
            (entry, marker) => Shape(entry).Marker = marker,
            entry => Shape(entry).MarkerSize,
            (entry, size) => Shape(entry).MarkerSize = size,
            entry => Shape(entry).MarkerEdge,
            (entry, ink) => Shape(entry).MarkerEdge = ink,
            entry => Shape(entry).MarkerFill,
            (entry, ink) => Shape(entry).MarkerFill = ink);

        AddWordProperty(table, "EdgeLighting",
            entry => LightingWord(Shape(entry).EdgeLighting),
            (entry, word, line, col) => Shape(entry).EdgeLighting = ToLighting("EdgeLighting", word, line, col));

        AddWordProperty(table, "BackFaceLighting",
            entry => Shape(entry).BackFaceLighting switch
            {
                Core.Drawing.BackFaceLighting.Unlit => "unlit",
                Core.Drawing.BackFaceLighting.Lit => "lit",
                _ => "reverselit",
            },
            (entry, word, line, col) => Shape(entry).BackFaceLighting = word switch
            {
                "unlit" => Core.Drawing.BackFaceLighting.Unlit,
                "lit" => Core.Drawing.BackFaceLighting.Lit,
                "reverselit" => Core.Drawing.BackFaceLighting.ReverseLit,
                _ => throw new JgsRuntimeException(line, col,
                    $"BackFaceLighting is 'unlit', 'lit' or 'reverselit', but got '{word}'."),
            });

        Put(table, "AlignVertexCenters",
            entry => OnOff(Shape(entry).AlignVertexCenters),
            (entry, value, line, col) => Shape(entry).AlignVertexCenters =
                ToOnOff("AlignVertexCenters", value, line, col));

        // A patch's normals come off its faces, so they are worked out rather than kept, exactly as
        // a surface's are — and refused a write for the same reason.
        Put(table, "FaceNormals", entry => PatchNormals(Shape(entry), perVertex: false),
            RefuseNormals("FaceNormals"));
        Put(table, "VertexNormals", entry => PatchNormals(Shape(entry), perVertex: true),
            RefuseNormals("VertexNormals"));
        AddWordProperty(table, "FaceNormalsMode", static _ => "auto", RefuseManual("FaceNormalsMode"));
        AddWordProperty(table, "VertexNormalsMode", static _ => "auto", RefuseManual("VertexNormalsMode"));
    }

    /// <summary>
    /// A patch's normals: one per face from the first three corners' cross product, or one per vertex
    /// averaged over the faces that meet there.
    /// </summary>
    private static JgsValue PatchNormals(PatchPlot shape, bool perVertex)
    {
        IReadOnlyList<int[]> faces = shape.Faces;
        int count = perVertex ? shape.X.Count : faces.Count;
        if (count == 0)
        {
            return JgsValue.Array([]);
        }

        var sums = new (double X, double Y, double Z)[count];
        var hits = new int[count];
        for (int f = 0; f < faces.Count; f++)
        {
            int[] face = faces[f];
            if (face.Length < 3)
            {
                continue;
            }

            (double x, double y, double z) = FaceNormal(shape, face);
            if (perVertex)
            {
                foreach (int v in face)
                {
                    sums[v] = (sums[v].X + x, sums[v].Y + y, sums[v].Z + z);
                    hits[v]++;
                }
            }
            else
            {
                sums[f] = (x, y, z);
                hits[f] = 1;
            }
        }

        var flat = new double[count * 3];
        for (int i = 0; i < count; i++)
        {
            (double x, double y, double z) = hits[i] == 0 ? (0, 0, 1) : sums[i];
            (double nx, double ny, double nz) = Unit(x, y, z);
            flat[i] = nx;
            flat[count + i] = ny;
            flat[(2 * count) + i] = nz;
        }

        return JgsMatrix.FromColumnMajor(flat, count, 3);
    }

    private static (double X, double Y, double Z) FaceNormal(PatchPlot shape, int[] face)
    {
        double ax = shape.X[face[1]] - shape.X[face[0]];
        double ay = shape.Y[face[1]] - shape.Y[face[0]];
        double az = shape.Z[face[1]] - shape.Z[face[0]];
        double bx = shape.X[face[2]] - shape.X[face[0]];
        double by = shape.Y[face[2]] - shape.Y[face[0]];
        double bz = shape.Z[face[2]] - shape.Z[face[0]];
        return ((ay * bz) - (az * by), (az * bx) - (ax * bz), (ax * by) - (ay * bx));
    }

    /// <summary>The four marker words, over whatever slots the object keeps them in.</summary>
    private static void AddMarkerBlock(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, MarkerType> readMarker,
        Action<JgsHandleEntry, MarkerType> writeMarker,
        Func<JgsHandleEntry, double> readSize,
        Action<JgsHandleEntry, double> writeSize,
        Func<JgsHandleEntry, Color?> readEdge,
        Action<JgsHandleEntry, Color?> writeEdge,
        Func<JgsHandleEntry, Color?> readFill,
        Action<JgsHandleEntry, Color?> writeFill)
    {
        Put(table, "Marker",
            entry => JgsValue.Str(JgsBuiltins.MarkerWord(readMarker(entry))),
            (entry, value, line, col) => writeMarker(entry,
                StrictMarker(JgsBuiltins.StrOf("Marker", value, line, col), line, col)));

        Put(table, "MarkerSize",
            entry => JgsValue.Number(readSize(entry)),
            (entry, value, line, col) =>
            {
                double size = Numbers("MarkerSize", value, 1, line, col)[0];
                if (size <= 0 || !double.IsFinite(size))
                {
                    throw new JgsRuntimeException(line, col, $"MarkerSize is a positive number, but got {size:G6}.");
                }

                writeSize(entry, size);
            });

        Put(table, "MarkerEdgeColor",
            entry => OptionalColorRow(readEdge(entry)),
            (entry, value, line, col) => writeEdge(entry, NoneOrColor(value, line, col, "marker")));

        Put(table, "MarkerFaceColor",
            entry => OptionalColorRow(readFill(entry)),
            (entry, value, line, col) => writeFill(entry, NoneOrColor(value, line, col, "marker")));
    }

    /// <summary>
    /// A marker word that refuses what it does not know. The verb-side parser falls back to the
    /// marker already in place, which is right when a line spec is being read out of a longer string
    /// and wrong for a property: <c>set(h,'Marker','wibble')</c> should say so.
    /// </summary>
    private static MarkerType StrictMarker(string word, int line, int col)
    {
        if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return MarkerType.None;
        }

        MarkerType guess = JgsBuiltins.ParseMarkerWord(word, MarkerType.None);
        if (guess == MarkerType.None)
        {
            throw new JgsRuntimeException(line, col,
                $"Marker is one of MATLAB's marker letters or 'none', but got '{word}'.");
        }

        return guess;
    }

    private static string LightingWord(SurfaceLighting lighting) => lighting switch
    {
        SurfaceLighting.Flat => "flat",
        SurfaceLighting.Gouraud => "gouraud",
        _ => "none",
    };

    private static SurfaceLighting ToLighting(string what, string word, int line, int col) => word switch
    {
        "none" => SurfaceLighting.None,
        "flat" => SurfaceLighting.Flat,
        "gouraud" => SurfaceLighting.Gouraud,
        _ => throw new JgsRuntimeException(
            line, col, $"{what} is 'none', 'flat' or 'gouraud', but got '{word}'."),
    };

    private static Action<JgsHandleEntry, JgsValue, int, int> RefuseNormals(string what) =>
        (_, _, line, col) => throw new JgsRuntimeException(line, col,
            $"{what} is worked out from the grid here and cannot be given: a normal this build did "
            + "not compute is one its lighting would not use.");

    private static Action<JgsHandleEntry, string, int, int> RefuseManual(string what) =>
        (_, word, line, col) =>
        {
            if (word != "auto")
            {
                throw new JgsRuntimeException(line, col,
                    $"{what} is always 'auto' here: the normals are worked out from the grid, so "
                    + "there is nothing to freeze.");
            }
        };

    private static double[] Counted(int count)
    {
        var ramp = new double[count];
        for (int i = 0; i < count; i++)
        {
            ramp[i] = i + 1;
        }

        return ramp;
    }

    /// <summary>
    /// A surface's normals as MATLAB reports them: rows-by-cols-by-3 per vertex, or one fewer of each
    /// per facet. Built by the same central-difference rule the lighting pass uses.
    /// </summary>
    private static JgsValue Normals(SurfacePlot sheet, bool perVertex)
    {
        double[,] z = sheet.Z;
        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        int outRows = perVertex ? rows : System.Math.Max(0, rows - 1);
        int outCols = perVertex ? cols : System.Math.Max(0, cols - 1);
        if (outRows == 0 || outCols == 0)
        {
            return JgsValue.Array([]);
        }

        var flat = new double[outRows * outCols * 3];
        int page = outRows * outCols;
        for (int r = 0; r < outRows; r++)
        {
            for (int c = 0; c < outCols; c++)
            {
                (double nx, double ny, double nz) = perVertex
                    ? VertexNormalAt(sheet, z, rows, cols, r, c)
                    : FacetNormalAt(sheet, z, r, c);

                // Column-major, because that is how a matrix leaves this layer.
                int i = (c * outRows) + r;
                flat[i] = nx;
                flat[page + i] = ny;
                flat[(2 * page) + i] = nz;
            }
        }

        JgsValue value = JgsMatrix.FromColumnMajor(flat, outRows * outCols, 3);
        value.ReshapeDims([outRows, outCols, 3]);
        return value;
    }

    /// <summary>The unit normal of one facet, from the cross product of its two diagonals.</summary>
    private static (double X, double Y, double Z) FacetNormalAt(SurfacePlot sheet, double[,] z, int r, int c)
    {
        double x0 = sheet.XAt(r, c);
        double x1 = sheet.XAt(r, c + 1);
        double y0 = sheet.YAt(r, c);
        double y1 = sheet.YAt(r + 1, c);

        double dzdx = x1 - x0 == 0 ? 0 : (z[r, c + 1] - z[r, c]) / (x1 - x0);
        double dzdy = y1 - y0 == 0 ? 0 : (z[r + 1, c] - z[r, c]) / (y1 - y0);
        return Unit(-dzdx, -dzdy, 1);
    }

    /// <summary>The unit normal at one vertex, from central differences where there are neighbours.</summary>
    private static (double X, double Y, double Z) VertexNormalAt(
        SurfacePlot sheet, double[,] z, int rows, int cols, int r, int c)
    {
        int cBack = System.Math.Max(0, c - 1);
        int cNext = System.Math.Min(cols - 1, c + 1);
        int rBack = System.Math.Max(0, r - 1);
        int rNext = System.Math.Min(rows - 1, r + 1);

        double dx = sheet.XAt(r, cNext) - sheet.XAt(r, cBack);
        double dy = sheet.YAt(rNext, c) - sheet.YAt(rBack, c);
        double dzdx = dx == 0 ? 0 : (z[r, cNext] - z[r, cBack]) / dx;
        double dzdy = dy == 0 ? 0 : (z[rNext, c] - z[rBack, c]) / dy;
        return Unit(-dzdx, -dzdy, 1);
    }

    private static (double X, double Y, double Z) Unit(double x, double y, double z)
    {
        double length = System.Math.Sqrt((x * x) + (y * y) + (z * z));
        return length > 0 ? (x / length, y / length, z / length) : (0, 0, 1);
    }

    // --- The heatmap ----------------------------------------------------------------------------

    /// <summary>
    /// What a heatmap shows and where it came from. The display block reorders and subsets the cells,
    /// which is what MATLAB's <c>XDisplayData</c> does; the table block re-runs the summary the chart
    /// was built by, which is what makes changing <c>ColorVariable</c> mean anything.
    /// </summary>
    private static void AddHeatmapBlock(IDictionary<string, GraphicsProperty> table)
    {
        static HeatmapPlot Cells(JgsHandleEntry entry) => (HeatmapPlot)entry.Target;

        AddChartLayout(table);

        Put(table, "FontName",
            entry => JgsValue.Str(Cells(entry).CellLabelStyle.FontFamily),
            (entry, value, line, col) =>
            {
                HeatmapPlot cells = Cells(entry);
                cells.CellLabelStyle = cells.CellLabelStyle.WithFamily(
                    JgsBuiltins.StrOf("FontName", value, line, col));
            });

        // The names the columns are known by, in the order they are shown. Writing them is how a
        // script reorders or narrows the chart: the values follow their own columns.
        Put(table, "XDisplayData",
            entry => Words(Cells(entry).ColumnLabels()),
            (entry, value, line, col) => ShowCells(entry, value, line, col, columns: true));
        Put(table, "YDisplayData",
            entry => Words(Cells(entry).RowLabels()),
            (entry, value, line, col) => ShowCells(entry, value, line, col, columns: false));

        Put(table, "XDisplayLabels",
            entry => Words(Cells(entry).ColumnText()),
            (entry, value, line, col) =>
            {
                HeatmapPlot cells = Cells(entry);
                cells.XDisplayLabels = LabelRow("XDisplayLabels", value, cells.Columns, line, col);
                cells.Axes?.LabelCells(cells);
            });
        Put(table, "YDisplayLabels",
            entry => Words(Cells(entry).RowText()),
            (entry, value, line, col) =>
            {
                HeatmapPlot cells = Cells(entry);
                cells.YDisplayLabels = LabelRow("YDisplayLabels", value, cells.Rows, line, col);
                cells.Axes?.LabelCells(cells);
            });

        // The grid as displayed, which after a reorder is the grid the chart holds — the two only
        // differ in MATLAB because it keeps the unshown categories, and a narrowed chart here has
        // genuinely dropped them.
        Put(table, "ColorDisplayData",
            entry => Grid(Cells(entry).ColorData),
            (entry, value, line, col) =>
            {
                HeatmapPlot cells = Cells(entry);
                cells.ColorData = JgsBuiltins.HeatmapGrid(value, line, col);
                cells.Axes?.LabelCells(cells);
            });

        // A pair of names rather than a pair of numbers, because the rulers of a heatmap are lists of
        // categories: the limits are the first and last one shown, and setting them cuts to that run.
        AddCategoryLimits(table, "XLimits", columns: true);
        AddCategoryLimits(table, "YLimits", columns: false);

        AddHeatmapSourceBlock(table);
    }

    /// <summary>Reorders or narrows a heatmap to the categories named, in the order they are named.</summary>
    private static void ShowCells(JgsHandleEntry entry, JgsValue value, int line, int col, bool columns)
    {
        var cells = (HeatmapPlot)entry.Target;
        string what = columns ? "XDisplayData" : "YDisplayData";
        string[] wanted = JgsRulerTicks.LabelWords(what, value, line, col);
        IReadOnlyList<string> have = columns ? cells.ColumnLabels() : cells.RowLabels();

        var picked = new List<int>(wanted.Length);
        foreach (string name in wanted)
        {
            int at = IndexOfName(have, name);
            if (at < 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what}: this heatmap has no {(columns ? "column" : "row")} called '{name}'. "
                    + $"It has {string.Join(", ", have)}.");
            }

            picked.Add(at);
        }

        if (picked.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} cannot be emptied — a heatmap showing nothing is not a heatmap.");
        }

        if (columns)
        {
            cells.ShowColumns(picked);
        }
        else
        {
            cells.ShowRows(picked);
        }

        cells.Axes?.LabelCells(cells);
    }

    /// <summary>The two ends of a categorical ruler, named rather than numbered.</summary>
    private static void AddCategoryLimits(
        IDictionary<string, GraphicsProperty> table, string name, bool columns)
    {
        string spelling = name;
        bool byColumn = columns;

        Put(table, spelling,
            entry =>
            {
                var cells = (HeatmapPlot)entry.Target;
                IReadOnlyList<string> have = byColumn ? cells.ColumnLabels() : cells.RowLabels();
                return have.Count == 0
                    ? JgsValue.Cell([])
                    : JgsValue.Cell([JgsValue.Str(have[0]), JgsValue.Str(have[^1])]);
            },
            (entry, value, line, col) =>
            {
                var cells = (HeatmapPlot)entry.Target;
                string[] ends = JgsRulerTicks.LabelWords(spelling, value, line, col);
                if (ends.Length != 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling} is the two categories at the ends of the run to show, "
                        + $"such as {{'a', 'c'}}.");
                }

                IReadOnlyList<string> have = byColumn ? cells.ColumnLabels() : cells.RowLabels();
                int from = IndexOfName(have, ends[0]);
                int to = IndexOfName(have, ends[1]);
                if (from < 0 || to < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling}: this heatmap has no {(byColumn ? "column" : "row")} called "
                        + $"'{(from < 0 ? ends[0] : ends[1])}'.");
                }

                var run = new List<int>();
                for (int i = System.Math.Min(from, to); i <= System.Math.Max(from, to); i++)
                {
                    run.Add(i);
                }

                if (byColumn)
                {
                    cells.ShowColumns(run);
                }
                else
                {
                    cells.ShowRows(run);
                }

                cells.Axes?.LabelCells(cells);
            });
    }

    private static int IndexOfName(IReadOnlyList<string> names, string wanted)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], wanted, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static JgsValue Words(IReadOnlyList<string> names) =>
        JgsValue.Cell([.. names.Select(JgsValue.Str)]);

    /// <summary>A row of labels, one per cell, or null to clear the overrides.</summary>
    private static string[]? LabelRow(string what, JgsValue value, int wanted, int line, int col)
    {
        string[] labels = JgsRulerTicks.LabelWords(what, value, line, col);
        if (labels.Length == 0)
        {
            return null;
        }

        if (labels.Length != wanted)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} needs one label per cell: {labels.Length} given, {wanted} wanted.");
        }

        return labels;
    }

    /// <summary>
    /// Where a heatmap's numbers came from. A chart given a matrix has no table, and answers each of
    /// these with nothing rather than inventing a source; a chart built from one re-summarises itself
    /// whenever a script changes which variable it groups or reduces by.
    /// </summary>
    private static void AddHeatmapSourceBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "SourceTable",
            entry => entry.HeatmapSource?.Table ?? JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                if (value.Type != JgsType.Table)
                {
                    throw new JgsRuntimeException(line, col,
                        "SourceTable is a table — the one whose rows this heatmap summarises.");
                }

                if (entry.HeatmapSource is not { } source)
                {
                    throw new JgsRuntimeException(line, col,
                        "SourceTable can only replace the table a heatmap was already built from: "
                        + "draw it with heatmap(tbl, xvar, yvar) to give it one.");
                }

                entry.HeatmapSource = new HeatmapSource
                {
                    Table = value,
                    XVariable = source.XVariable,
                    YVariable = source.YVariable,
                    ColorVariable = source.ColorVariable,
                    ColorMethod = source.ColorMethod,
                };
                JgsBuiltins.ResummariseHeatmap(entry, line, col);
            });

        AddSourceVariable(table, "XVariable",
            source => source.XVariable, (source, name) => source.XVariable = name);
        AddSourceVariable(table, "YVariable",
            source => source.YVariable, (source, name) => source.YVariable = name);
        // Naming a variable to reduce over moves the method off counting, which is MATLAB's rule and
        // the only one that makes the write mean anything: counting ignores the variable entirely.
        AddSourceVariable(table, "ColorVariable",
            source => source.ColorVariable,
            (source, name) =>
            {
                source.ColorVariable = name;
                if (name.Length > 0 && source.ColorMethod == "count")
                {
                    source.ColorMethod = "mean";
                }
                else if (name.Length == 0)
                {
                    source.ColorMethod = "count";
                }
            });

        Put(table, "ColorMethod",
            entry => JgsValue.Str(entry.HeatmapSource?.ColorMethod ?? "none"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("ColorMethod", value, line, col).ToLowerInvariant();
                if (word is not ("count" or "mean" or "median" or "sum" or "none"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"ColorMethod is 'count', 'mean', 'median', 'sum' or 'none', but got '{word}'.");
                }

                RequireSource(entry, "ColorMethod", line, col).ColorMethod = word;
                JgsBuiltins.ResummariseHeatmap(entry, line, col);
            });
    }

    private static void AddSourceVariable(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<HeatmapSource, string> read,
        Action<HeatmapSource, string> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => JgsValue.Str(entry.HeatmapSource is { } source ? read(source) : string.Empty),
            (entry, value, line, col) =>
            {
                write(RequireSource(entry, spelling, line, col),
                    JgsBuiltins.StrOf(spelling, value, line, col));
                JgsBuiltins.ResummariseHeatmap(entry, line, col);
            });
    }

    private static HeatmapSource RequireSource(JgsHandleEntry entry, string what, int line, int col) =>
        entry.HeatmapSource ?? throw new JgsRuntimeException(line, col,
            $"{what} names a variable of the table a heatmap was summarised from, and this one was "
            + "given its numbers directly — draw it with heatmap(tbl, xvar, yvar) to set one.");

    // --- The contour ----------------------------------------------------------------------------

    /// <summary>
    /// A contour's levels, the ink it traces them in, and which of them carry their own value. Three
    /// of the properties here are steps rather than lists — <c>LevelStep</c>, <c>TextStep</c> — and
    /// each outranks the list beside it, which is the order MATLAB resolves them in too.
    /// </summary>
    private static void AddContourBlock(IDictionary<string, GraphicsProperty> table)
    {
        static ContourPlot Map(JgsHandleEntry entry) => (ContourPlot)entry.Target;

        Put(table, "Fill",
            entry => OnOff(Map(entry).Filled),
            (entry, value, line, col) => Map(entry).Filled = ToOnOff("Fill", value, line, col));

        AddNullableMode(table, "LevelListMode",
            entry => Map(entry).Levels is { Length: > 0 },
            entry => Map(entry).Levels = Map(entry).ResolvedLevels,
            entry => Map(entry).Levels = null);

        Put(table, "LevelStep",
            entry => JgsValue.Number(Map(entry).LevelStep ?? StepOf(Map(entry).ResolvedLevels)),
            (entry, value, line, col) =>
            {
                double step = Numbers("LevelStep", value, 1, line, col)[0];
                if (step <= 0 || !double.IsFinite(step))
                {
                    throw new JgsRuntimeException(
                        line, col, $"LevelStep is a positive number, but got {step:G6}.");
                }

                ContourPlot map = Map(entry);
                map.Levels = null;
                map.LevelStep = step;
            });

        AddNullableMode(table, "LevelStepMode",
            entry => Map(entry).LevelStep is not null,
            entry => Map(entry).LevelStep ??= StepOf(Map(entry).ResolvedLevels),
            entry => Map(entry).LevelStep = null);

        // MATLAB's word for the levels that carry text; the model has always called them label levels.
        Put(table, "TextList",
            entry => Row(Map(entry).LabelLevels ?? Map(entry).ResolvedLevels),
            (entry, value, line, col) =>
            {
                double[] wanted = JgsBuiltins.ToDoubles("TextList", value, line, col);
                Map(entry).LabelLevels = wanted.Length == 0 ? null : wanted;
            });

        AddNullableMode(table, "TextListMode",
            entry => Map(entry).LabelLevels is { Length: > 0 },
            entry => Map(entry).LabelLevels = Map(entry).ResolvedLevels,
            entry => Map(entry).LabelLevels = null);

        Put(table, "TextStep",
            entry => JgsValue.Number(Map(entry).TextStep ?? StepOf(Map(entry).ResolvedLevels)),
            (entry, value, line, col) =>
            {
                double step = Numbers("TextStep", value, 1, line, col)[0];
                if (step <= 0 || !double.IsFinite(step))
                {
                    throw new JgsRuntimeException(
                        line, col, $"TextStep is a positive number, but got {step:G6}.");
                }

                Map(entry).TextStep = step;
            });

        AddNullableMode(table, "TextStepMode",
            entry => Map(entry).TextStep is not null,
            entry => Map(entry).TextStep ??= StepOf(Map(entry).ResolvedLevels),
            entry => Map(entry).TextStep = null);

        Put(table, "LabelSpacing",
            entry => JgsValue.Number(Map(entry).LabelSpacing),
            (entry, value, line, col) =>
            {
                double spacing = Numbers("LabelSpacing", value, 1, line, col)[0];
                if (spacing < 0 || !double.IsFinite(spacing))
                {
                    throw new JgsRuntimeException(line, col, "LabelSpacing cannot be negative.");
                }

                Map(entry).LabelSpacing = spacing;
            });

        // 'flat' is MATLAB's word for a curve coloured by its own level, which is what an unset ink
        // means here — so the word reads and writes the null rather than a colour standing in for it.
        Put(table, "LineColor",
            entry => Map(entry).LineColor is { } ink ? ColorRow(ink) : JgsValue.Str("flat"),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.String
                    && JgsBuiltins.StrOf("LineColor", value, line, col) is { } word
                    && (word.Equals("flat", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("none", StringComparison.OrdinalIgnoreCase)))
                {
                    // 'none' hides the curves, which this build says with the fill switch: a contour
                    // with no lines and no bands would be an object that draws nothing at all.
                    if (word.Equals("none", StringComparison.OrdinalIgnoreCase) && !Map(entry).Filled)
                    {
                        throw new JgsRuntimeException(line, col,
                            "LineColor 'none' on an unfilled contour would draw nothing — turn Fill on "
                            + "first, or give the curves a colour.");
                    }

                    Map(entry).LineColor = null;
                    return;
                }

                Map(entry).LineColor = JgsBuiltins.OptionColor(value, line, col, "contour");
            });

        Put(table, "LineStyle",
            entry => JgsValue.Str(DashWord(Map(entry).LineDash)),
            (entry, value, line, col) => Map(entry).LineDash =
                ToDash("LineStyle", JgsBuiltins.StrOf("LineStyle", value, line, col), line, col));

        AddWordProperty(table, "ZLocation",
            entry => Map(entry).ContoursAtZero ? "zero" : "auto",
            (entry, word, line, col) => Map(entry).ContoursAtZero = word switch
            {
                "auto" => false,
                "zero" => true,
                _ => throw new JgsRuntimeException(
                    line, col, $"ZLocation is 'auto' or 'zero', but got '{word}'."),
            });

        // The matrix a script reads the curves out of, in the two-row form clabel and contourc use.
        Put(table, "ContourMatrix", entry => JgsBuiltins.ContourMatrixFor(Map(entry)));

        AddNullableMode(table, "XDataMode",
            entry => !Map(entry).XImplied,
            entry => Map(entry).XImplied = false,
            entry =>
            {
                ContourPlot map = Map(entry);
                map.SetData(Counted(map.Z.GetLength(1)), map.Y, map.Z);
                map.XImplied = true;
            });

        AddNullableMode(table, "YDataMode",
            entry => !Map(entry).YImplied,
            entry => Map(entry).YImplied = false,
            entry =>
            {
                ContourPlot map = Map(entry);
                map.SetData(map.X, Counted(map.Z.GetLength(0)), map.Z);
                map.YImplied = true;
            });
    }

    /// <summary>The gap between neighbouring levels, which is what a step means when none was given.</summary>
    private static double StepOf(double[] levels) =>
        levels.Length < 2 ? 1 : System.Math.Abs(levels[1] - levels[0]);

    // --- The colorbar ---------------------------------------------------------------------------

    /// <summary>
    /// The colorbar's side, its ruler and its ink. The ruler half is the interesting one: a colorbar
    /// is the only object in this build that carries a scale without carrying an <see cref="AxisModel"/>,
    /// so its ticks, limits and labels are stored on the bar itself and answer to the same
    /// nullable-means-auto idiom every ruler here uses.
    /// </summary>
    private static void AddColorbarBlock(IDictionary<string, GraphicsProperty> table)
    {
        static ColorbarModel Bar(JgsHandleEntry entry) => (ColorbarModel)entry.Target;

        AddTextStyleBlock(table,
            entry => Bar(entry).TickLabelStyle,
            (entry, style) => Bar(entry).TickLabelStyle = style);

        // MATLAB spells the interpreter of a ruler's labels out in full, and it is the same choice the
        // shared block writes — so the long name is put over the short one rather than beside it.
        GraphicsProperty markup = table["Interpreter"];
        table["TickLabelInterpreter"] =
            new GraphicsProperty("TickLabelInterpreter", markup.Read, markup.Write);
        table.Remove("Interpreter");

        AddWordProperty(table, "Location",
            entry => LocationWord(Bar(entry).Location),
            (entry, word, line, col) =>
            {
                ColorbarModel bar = Bar(entry);
                bar.Location = word switch
                {
                    "eastoutside" => ColorbarLocation.EastOutside,
                    "westoutside" => ColorbarLocation.WestOutside,
                    "northoutside" => ColorbarLocation.NorthOutside,
                    "southoutside" => ColorbarLocation.SouthOutside,
                    "east" => ColorbarLocation.East,
                    "west" => ColorbarLocation.West,
                    "north" => ColorbarLocation.North,
                    "south" => ColorbarLocation.South,
                    "manual" => ColorbarLocation.Manual,
                    _ => throw new JgsRuntimeException(line, col,
                        $"Location is one of 'north' 'south' 'east' 'west', those four with 'outside' "
                        + $"after them, or 'manual', but got '{word}'."),
                };

                // Naming a side is how a script takes a pinned bar off its pin, which is what makes
                // colorbar('Location','north') work on a bar that was placed by hand.
                if (bar.Location != ColorbarLocation.Manual)
                {
                    bar.FigureBox = null;
                }
            });

        // A colorbar shows the plot's colour range until a script narrows it, and answers with the
        // range being shown either way — an unset slot is not an answer a script can use.
        Put(table, "Limits",
            entry => Row(SpanOf(Bar(entry)).Min, SpanOf(Bar(entry)).Max),
            (entry, value, line, col) =>
            {
                double[] pair = Numbers("Limits", value, 2, line, col);
                Bar(entry).Limits = pair[1] > pair[0]
                    ? new DataRange(pair[0], pair[1])
                    : throw new JgsRuntimeException(line, col,
                        "Limits is two increasing numbers, such as [0 10].");
            });

        AddNullableMode(table, "LimitsMode",
            entry => Bar(entry).Limits is not null,
            entry => Bar(entry).Limits ??= SpanOf(Bar(entry)),
            entry => Bar(entry).Limits = null);

        Put(table, "Ticks",
            entry => Row(Bar(entry).TickValues ?? GeneratedTicks(Bar(entry))),
            (entry, value, line, col) =>
            {
                double[] ticks = JgsBuiltins.ToDoubles("Ticks", value, line, col);
                Bar(entry).TickValues = ticks.Length == 0 ? [] : ticks;
            });

        AddNullableMode(table, "TicksMode",
            entry => Bar(entry).TickValues is not null,
            entry => Bar(entry).TickValues ??= GeneratedTicks(Bar(entry)),
            entry => Bar(entry).TickValues = null);

        Put(table, "TickLabels",
            entry => JgsValue.Cell([.. TickLabelsOf(Bar(entry)).Select(JgsValue.Str)]),
            (entry, value, line, col) =>
            {
                string[] labels = JgsRulerTicks.LabelWords("TickLabels", value, line, col);
                Bar(entry).TickLabelOverrides = labels;
            });

        AddNullableMode(table, "TickLabelsMode",
            entry => Bar(entry).TickLabelOverrides is not null,
            entry => Bar(entry).TickLabelOverrides ??= [.. TickLabelsOf(Bar(entry))],
            entry => Bar(entry).TickLabelOverrides = null);

        AddWordProperty(table, "TickDirection",
            entry => Bar(entry).TickDirection.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Bar(entry).TickDirection = word switch
            {
                "in" => TickDirection.In,
                "out" => TickDirection.Out,
                "both" => TickDirection.Both,
                _ => throw new JgsRuntimeException(
                    line, col, $"TickDirection is 'in', 'out', or 'both', but got '{word}'."),
            });

        Put(table, "TickLength",
            entry => JgsValue.Number(Bar(entry).TickLength),
            (entry, value, line, col) =>
            {
                double length = Numbers("TickLength", value, 1, line, col)[0];
                if (length < 0 || !double.IsFinite(length))
                {
                    throw new JgsRuntimeException(line, col, "TickLength cannot be negative.");
                }

                Bar(entry).TickLength = length;
            });

        AddWordProperty(table, "Direction",
            entry => Bar(entry).Inverted ? "reverse" : "normal",
            (entry, word, line, col) => Bar(entry).Inverted = word switch
            {
                "normal" => false,
                "reverse" => true,
                _ => throw new JgsRuntimeException(
                    line, col, $"Direction is 'normal' or 'reverse', but got '{word}'."),
            });

        AddWordProperty(table, "AxisLocation",
            entry => Bar(entry).LabelsInside ? "in" : "out",
            (entry, word, line, col) => Bar(entry).LabelsInside = word switch
            {
                "out" => false,
                "in" => true,
                _ => throw new JgsRuntimeException(
                    line, col, $"AxisLocation is 'out' or 'in', but got '{word}'."),
            });

        // Which face the labels are written on is a choice with no automatic answer to fall back to,
        // so its mode says 'manual' the moment a script names a face and 'auto' until then.
        AddNullableMode(table, "AxisLocationMode",
            entry => Bar(entry).LabelsInside,
            entry => Bar(entry).LabelsInside = true,
            entry => Bar(entry).LabelsInside = false);

        Put(table, "Box",
            entry => OnOff(Bar(entry).BoxVisible),
            (entry, value, line, col) => Bar(entry).BoxVisible = ToOnOff("Box", value, line, col));

        // One word for two inks, as MATLAB has it: the outline and the ticks take the colour, and so
        // do the labels, which keep it in their own style because that is where a font lives.
        Put(table, "Color",
            entry => ColorRow(Bar(entry).Ink ?? Bar(entry).TickLabelStyle.Color),
            (entry, value, line, col) =>
            {
                ColorbarModel bar = Bar(entry);
                Color ink = JgsBuiltins.OptionColor(value, line, col, "colorbar");
                bar.Ink = ink;
                bar.TickLabelStyle = bar.TickLabelStyle.WithColor(ink);
            });

        Put(table, "LineWidth",
            entry => JgsValue.Number(Bar(entry).LineWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width < 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth cannot be negative, but got {width:G6}.");
                }

                Bar(entry).LineWidth = width;
            });

        AddFurniturePosition(table,
            entry => Bar(entry).LastBox,
            entry => Bar(entry).FigureBox,
            (entry, box) =>
            {
                ColorbarModel bar = Bar(entry);
                bar.FigureBox = box;
                bar.Location = ColorbarLocation.Manual;
            },
            entry => Bar(entry).Parent as AxesModel);
    }

    private static string LocationWord(ColorbarLocation location) => location switch
    {
        ColorbarLocation.WestOutside => "westoutside",
        ColorbarLocation.NorthOutside => "northoutside",
        ColorbarLocation.SouthOutside => "southoutside",
        ColorbarLocation.East => "east",
        ColorbarLocation.West => "west",
        ColorbarLocation.North => "north",
        ColorbarLocation.South => "south",
        ColorbarLocation.Manual => "manual",
        _ => "eastoutside",
    };

    /// <summary>The values the strip spans — its own limits, or the colour range of what it legends.</summary>
    private static DataRange SpanOf(ColorbarModel bar)
    {
        if (bar.Limits is { } chosen)
        {
            return chosen;
        }

        if (bar.Parent is AxesModel axes)
        {
            foreach (PlotObject plot in axes.Plots)
            {
                if (plot.Visible && plot is IColorMapped { HasMappedData: true } mapped)
                {
                    (double min, double max) = mapped.ColorRange;
                    return max > min ? new DataRange(min, max) : new DataRange(min, min + 1);
                }
            }
        }

        return new DataRange(0, 1);
    }

    /// <summary>
    /// The tick values the strip would generate for the span it is showing — the same generator the
    /// renderer runs, so what a script reads back is what the drawing puts there.
    /// </summary>
    private static double[] GeneratedTicks(ColorbarModel bar)
    {
        DataRange span = SpanOf(bar);
        return [.. new LinearTickGenerator().Generate(span, 6).MajorTicks.Select(static t => t.Value)];
    }

    /// <summary>The labels the strip shows: the chosen ones, or the numbers under the generated ticks.</summary>
    private static IReadOnlyList<string> TickLabelsOf(ColorbarModel bar)
    {
        double[] ticks = bar.TickValues ?? GeneratedTicks(bar);
        if (bar.TickLabelOverrides is { Length: > 0 } chosen)
        {
            return [.. ticks.Select((_, i) => chosen[i % chosen.Length])];
        }

        return [.. ticks.Select(static value =>
            value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture))];
    }

    // --- A text label ---------------------------------------------------------------------------

    /// <summary>
    /// What a label is written in and where it reaches to. The font block is shared with the legend
    /// and the colorbar; the rest is a label's own — the turn, the edge round its box, the space
    /// inside that box, and the two words that say a label is not being typed into and is measured in
    /// the data it sits among.
    /// </summary>
    private static void AddTextBlock(IDictionary<string, GraphicsProperty> table)
    {
        static TextAnnotation Label(JgsHandleEntry entry) => (TextAnnotation)entry.Target;

        // The label keeps its font in loose pieces rather than a TextStyle, so one is assembled for
        // the shared block and taken apart again on the way back. Colour is left out: a label's ink
        // is nullable — 'auto' means the axes' — and the shared block has no word for that.
        AddTextStyleBlock(table,
            entry =>
            {
                TextAnnotation label = Label(entry);
                return new TextStyle(
                    label.Color ?? Colors.Black, label.FontSize, label.FontFamily,
                    label.Bold, label.Italic, label.Interpreter, label.Smoothing);
            },
            (entry, style) =>
            {
                TextAnnotation label = Label(entry);
                label.FontSize = style.FontSize;
                label.FontFamily = style.FontFamily;
                label.Bold = style.Bold;
                label.Italic = style.Italic;
                label.Interpreter = style.Interpreter;
                label.Smoothing = style.Antialias;
            });

        Put(table, "Margin",
            entry => JgsValue.Number(Label(entry).Padding),
            (entry, value, line, col) =>
            {
                double margin = Numbers("Margin", value, 1, line, col)[0];
                if (margin < 0 || !double.IsFinite(margin))
                {
                    throw new JgsRuntimeException(line, col, $"Margin cannot be negative, but got {margin:G6}.");
                }

                Label(entry).Padding = margin;
            });

        Put(table, "LineWidth",
            entry => JgsValue.Number(Label(entry).BorderWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width < 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth cannot be negative, but got {width:G6}.");
                }

                Label(entry).BorderWidth = width;
            });

        Put(table, "LineStyle",
            entry => JgsValue.Str(DashWord(Label(entry).BorderDash)),
            (entry, value, line, col) => Label(entry).BorderDash =
                ToDash("LineStyle", JgsBuiltins.StrOf("LineStyle", value, line, col), line, col));

        // A label is anchored either among the data or in the figure, and that is exactly the choice
        // MATLAB spells with these two words.
        AddWordProperty(table, "Units",
            entry => Label(entry).Space == AnnotationSpace.Figure ? "normalized" : "data",
            (entry, word, line, col) => Label(entry).Space = word switch
            {
                "data" => AnnotationSpace.Data,
                "normalized" => AnnotationSpace.Figure,
                _ => throw new JgsRuntimeException(line, col,
                    $"Text units are 'data' or 'normalized' here, but got '{word}'."),
            });

        // Typing into a label in the figure is the plot browser's business, not a script's, and there
        // is no in-place editor to switch on — so the word answers and only the false one is accepted.
        AddWordProperty(table, "Editing",
            static _ => "off",
            static (_, word, line, col) =>
            {
                if (word != "off")
                {
                    throw new JgsRuntimeException(line, col,
                        "Editing turns on an in-place text cursor, which this build does not have — "
                        + "edit the label through its String property or the plot browser.");
                }
            });

        // Extent is a measurement of a drawing, so it answers from the last frame rather than from
        // the document: a label that has never been drawn has no measured size, and reports the empty
        // box at its own anchor rather than a guess at what a font it has not seen would come to.
        Put(table, "Extent", entry => ExtentOf(Label(entry)));
    }

    /// <summary>
    /// The label's rendered rectangle in the units it is placed in. Pixels come back through the plot
    /// box the renderer last reported, inverted one ruler at a time so a log or reversed direction
    /// reads back the value that was drawn there.
    /// </summary>
    private static JgsValue ExtentOf(TextAnnotation label)
    {
        Rect2D pixels = label.RenderedBounds;
        if (pixels.Width <= 0 && pixels.Height <= 0)
        {
            return Row(label.Position.X, label.Position.Y, 0, 0);
        }

        if (label.Parent is not AxesModel axes || axes.LastLayout is not { } layout)
        {
            return Row(label.Position.X, label.Position.Y, 0, 0);
        }

        if (label.Space == AnnotationSpace.Figure)
        {
            Rect2D box = layout.Normalize(pixels);
            return Row(box.X, 1 - box.Y - box.Height, box.Width, box.Height);
        }

        Rect2D area = layout.PlotAreaPx;
        double left = Invert(axes.PrimaryXAxis, pixels.Left, area.Left, area.Width, false);
        double right = Invert(axes.PrimaryXAxis, pixels.Right, area.Left, area.Width, false);
        double bottom = Invert(axes.PrimaryYAxis, pixels.Bottom, area.Top, area.Height, true);
        double top = Invert(axes.PrimaryYAxis, pixels.Top, area.Top, area.Height, true);

        return Row(
            System.Math.Min(left, right),
            System.Math.Min(bottom, top),
            System.Math.Abs(right - left),
            System.Math.Abs(top - bottom));
    }

    /// <summary>Turns one device coordinate back into the value the ruler draws there.</summary>
    private static double Invert(AxisModel ruler, double pixel, double origin, double span, bool downward)
    {
        if (span <= 0)
        {
            return ruler.Range.Min;
        }

        double fraction = (pixel - origin) / span;
        if (downward)
        {
            fraction = 1 - fraction;
        }

        if (ruler.Inverted)
        {
            fraction = 1 - fraction;
        }

        DataRange range = ruler.Range;
        if (ruler.Scale == AxisScaleType.Logarithmic && range.Min > 0 && range.Max > 0)
        {
            double low = System.Math.Log10(range.Min);
            return System.Math.Pow(10, low + (fraction * (System.Math.Log10(range.Max) - low)));
        }

        return range.Min + (fraction * (range.Max - range.Min));
    }

    /// <summary>
    /// The bubble legend's box, ink and font. It is the legend's block with one property of its own —
    /// which end of the size range is listed first — because the two are the same piece of furniture
    /// and a script styling one expects the same words to work on the other.
    /// </summary>
    private static void AddBubbleLegendBlock(IDictionary<string, GraphicsProperty> table)
    {
        static BubbleLegendModel Sizes(JgsHandleEntry entry) => (BubbleLegendModel)entry.Target;

        AddTextStyleBlock(table,
            entry => Sizes(entry).TextStyle,
            (entry, style) => Sizes(entry).TextStyle = style,
            colorName: "TextColor");

        Put(table, "Color",
            entry => ColorRow(Sizes(entry).Background),
            (entry, value, line, col) => Sizes(entry).Background =
                JgsBuiltins.OptionColor(value, line, col, "bubblelegend"));

        Put(table, "EdgeColor",
            entry => ColorRow(Sizes(entry).BorderColor),
            (entry, value, line, col) => Sizes(entry).BorderColor =
                JgsBuiltins.OptionColor(value, line, col, "bubblelegend"));

        Put(table, "LineWidth",
            entry => JgsValue.Number(Sizes(entry).BorderWidth),
            (entry, value, line, col) =>
            {
                double width = Numbers("LineWidth", value, 1, line, col)[0];
                if (width < 0 || !double.IsFinite(width))
                {
                    throw new JgsRuntimeException(line, col, $"LineWidth cannot be negative, but got {width:G6}.");
                }

                Sizes(entry).BorderWidth = width;
            });

        AddWordProperty(table, "BubbleSizeOrder",
            entry => Sizes(entry).Descending ? "descend" : "ascend",
            (entry, word, line, col) => Sizes(entry).Descending = word switch
            {
                "ascend" => false,
                "descend" => true,
                _ => throw new JgsRuntimeException(
                    line, col, $"BubbleSizeOrder is 'ascend' or 'descend', but got '{word}'."),
            });

        AddFurniturePosition(table,
            entry => Sizes(entry).LastBox,
            entry => Sizes(entry).FigureBox,
            (entry, box) =>
            {
                BubbleLegendModel sizes = Sizes(entry);
                sizes.FigureBox = box;
                sizes.Position = LegendPosition.Custom;
            },
            entry => Sizes(entry).Parent as AxesModel);
    }

    /// <summary>
    /// <c>Position</c> and <c>Units</c> for a box that floats in a figure rather than owning a cell in
    /// it — a legend or a colorbar. The rectangle is in figure fractions measured up from the bottom,
    /// which is MATLAB's convention and the flip <see cref="FlipRow"/> already performs for an axes;
    /// reading answers where the thing was drawn until a script pins it, and thereafter what it pinned.
    /// </summary>
    private static void AddFurniturePosition(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, Rect2D?> drawn,
        Func<JgsHandleEntry, Rect2D?> pinned,
        Action<JgsHandleEntry, Rect2D> pin,
        Func<JgsHandleEntry, AxesModel?> owner)
    {
        Put(table, "Position",
            entry =>
            {
                if (pinned(entry) is { } chosen)
                {
                    return FlipRow(chosen);
                }

                if (drawn(entry) is { } box && owner(entry) is { LastLayout: { } layout })
                {
                    return FlipRow(layout.Normalize(box));
                }

                // Nothing has been drawn and nothing pinned: the honest answer is the empty box at
                // the origin, not a guess at where a frame that never happened would have put it.
                return Row(0, 0, 0, 0);
            },
            (entry, value, line, col) => pin(entry, FlipRect(Box("Position", value, line, col))));

        Put(table, "Units",
            static _ => JgsValue.Str("normalized"),
            static (_, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("Units", value, line, col);
                if (!word.Equals("normalized", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"This build places figure furniture in fractions of the figure, and '{word}' "
                        + "is not a unit it measures in.");
                }
            });
    }
}
