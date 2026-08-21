using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The M74 axes property families: the camera the picture is drawn from, the alpha mapping that
/// turns data into transparency, and the four odd ones out — clipping, face ordering, and where the
/// pointer last crossed the axes. The camera properties are the mode idiom again, a nullable slot on
/// the model read as auto or manual, so an axes nobody has placed a camera on draws exactly as it did
/// before this wave.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static void AddAxesWaveTwo(IDictionary<string, GraphicsProperty> table)
    {
        AddCameraBlock(table);
        AddAlphaBlock(table);
        AddClippingBlock(table);
    }

    // --- The camera -----------------------------------------------------------------------------

    private static void AddCameraBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddCameraVector(
            table, "CameraPosition",
            axes => axes.EffectiveCameraPosition(),
            axes => axes.CameraPosition,
            (axes, value) =>
            {
                axes.CameraPosition = value;

                // Keep the angles describing where the camera now looks from, so View and campos do
                // not contradict each other. Only the direction is recoverable, which is all the
                // angles ever carried.
                if (value is { } placed)
                {
                    SyncAnglesToPosition(axes, placed);
                }
            });

        AddCameraVector(
            table, "CameraTarget",
            axes => axes.EffectiveCameraTarget(),
            axes => axes.CameraTarget,
            (axes, value) => axes.CameraTarget = value);

        AddCameraVector(
            table, "CameraUpVector",
            axes => axes.EffectiveCameraUpVector(),
            axes => axes.CameraUpVector,
            (axes, value) => axes.CameraUpVector = value);

        Put(table, "CameraViewAngle",
            entry => JgsValue.Number(Axes(entry).EffectiveCameraViewAngle()),
            (entry, value, line, col) =>
            {
                double angle = Numbers("CameraViewAngle", value, 1, line, col)[0];
                if (!(angle > 0 && angle < 180))
                {
                    throw new JgsRuntimeException(line, col,
                        $"CameraViewAngle is an angle strictly between 0 and 180 degrees, but got {angle}.");
                }

                Axes(entry).CameraViewAngle = angle;
            });

        Put(table, "CameraViewAngleMode",
            entry => AutoManual(Axes(entry).CameraViewAngle is not null),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                axes.CameraViewAngle = ToAutoManual("CameraViewAngleMode", value, line, col)
                    ? axes.EffectiveCameraViewAngle()
                    : null;
            });

        Put(table, "Projection",
            entry => JgsValue.Str(
                Axes(entry).Projection == ProjectionType.Perspective ? "perspective" : "orthographic"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("Projection", value, line, col).ToLowerInvariant();
                Axes(entry).Projection = word switch
                {
                    "orthographic" => ProjectionType.Orthographic,
                    "perspective" => ProjectionType.Perspective,
                    _ => throw new JgsRuntimeException(line, col,
                        $"Projection is 'orthographic' or 'perspective', but got '{word}'."),
                };
            });
    }

    /// <summary>
    /// One of the three camera vectors and its mode word. Reading answers where the camera is,
    /// chosen or derived; writing puts it there; and the mode says which of the two that was —
    /// 'manual' freezing what is showing, 'auto' handing it back to the view angles.
    /// </summary>
    private static void AddCameraVector(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<AxesModel, Vector3D> effective,
        Func<AxesModel, Vector3D?> slot,
        Action<AxesModel, Vector3D?> write)
    {
        Put(table, name,
            entry =>
            {
                Vector3D current = effective(Axes(entry));
                return Row(current.X, current.Y, current.Z);
            },
            (entry, value, line, col) =>
            {
                double[] xyz = Numbers(name, value, 3, line, col);
                foreach (double component in xyz)
                {
                    if (!double.IsFinite(component))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name} must be three finite numbers, but got {component}.");
                    }
                }

                write(Axes(entry), new Vector3D(xyz[0], xyz[1], xyz[2]));
            });

        Put(table, name + "Mode",
            entry => AutoManual(slot(Axes(entry)) is not null),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                write(axes, ToAutoManual(name + "Mode", value, line, col) ? effective(axes) : null);
            });
    }

    /// <summary>
    /// Reads the view angles back off a camera that has just been placed. The scaling is undone first
    /// so that a tall Z axis does not tip the camera on its own — this is the normalized box the
    /// projection itself works in.
    /// </summary>
    private static void SyncAnglesToPosition(AxesModel axes, Vector3D position)
    {
        Vector3D target = axes.EffectiveCameraTarget();
        double xSpan = Span(axes.PrimaryXAxis.Range);
        double ySpan = Span(axes.ActiveYAxis.Range);
        double zSpan = Span(axes.ZAxis.Range);

        double dx = (position.X - target.X) / xSpan;
        double dy = (position.Y - target.Y) / ySpan;
        double dz = (position.Z - target.Z) / zSpan;
        double horizontal = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (horizontal < 1e-12 && System.Math.Abs(dz) < 1e-12)
        {
            // Standing on the target names no direction; leave the angles saying what they said.
            return;
        }

        axes.Azimuth = System.Math.Atan2(dx, -dy) * 180 / System.Math.PI;
        axes.Elevation = System.Math.Atan2(dz, horizontal) * 180 / System.Math.PI;

        static double Span(DataRange range)
        {
            double span = range.Max - range.Min;
            return System.Math.Abs(span) < 1e-300 ? 1 : span;
        }
    }

    // --- Alpha mapping --------------------------------------------------------------------------

    private static void AddAlphaBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "ALim",
            entry =>
            {
                DataRange current = CurrentAlphaRange(Axes(entry));
                return Row(current.Min, current.Max);
            },
            (entry, value, line, col) =>
            {
                double[] pair = Numbers("ALim", value, 2, line, col);
                if (!double.IsFinite(pair[0]) || !double.IsFinite(pair[1]) || pair[0] >= pair[1])
                {
                    throw new JgsRuntimeException(line, col,
                        $"ALim must be finite and increasing, but got [{pair[0]}, {pair[1]}].");
                }

                Axes(entry).AlphaLimits = new DataRange(pair[0], pair[1]);
            });

        Put(table, "ALimMode",
            entry => AutoManual(Axes(entry).AlphaLimits is not null),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                if (ToAutoManual("ALimMode", value, line, col))
                {
                    // Freeze what is showing, the way CLimMode does.
                    axes.AlphaLimits ??= CurrentAlphaRange(axes);
                }
                else
                {
                    axes.AlphaLimits = null;
                }
            });

        Put(table, "Alphamap",
            entry =>
            {
                IReadOnlyList<double> map = Axes(entry).Alphamap ?? AlphaSampler.DefaultMap;
                return JgsMatrix.FromColumnMajor([.. map], 1, map.Count);
            },
            (entry, value, line, col) =>
            {
                double[] map = JgsBuiltins.ToDoubles("Alphamap", value, line, col);
                if (map.Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "Alphamap needs at least one transparency.");
                }

                foreach (double transparency in map)
                {
                    if (!double.IsFinite(transparency) || transparency < 0 || transparency > 1)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"Alphamap entries are between 0 and 1, but got {transparency}.");
                    }
                }

                Axes(entry).Alphamap = map;
            });

        Put(table, "AlphaScale",
            entry => JgsValue.Str(Axes(entry).AlphaScale == ColorScaleType.Log ? "log" : "linear"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("AlphaScale", value, line, col).ToLowerInvariant();
                Axes(entry).AlphaScale = word switch
                {
                    "linear" => ColorScaleType.Linear,
                    "log" => ColorScaleType.Log,
                    _ => throw new JgsRuntimeException(line, col,
                        $"AlphaScale is 'linear' or 'log', but got '{word}'."),
                };
            });
    }

    /// <summary>
    /// The alpha limits in force: the ones pinned, else the extent of the first alpha data being
    /// drawn, else the whole unit range — the same shape of answer CLim gives for color.
    /// </summary>
    private static DataRange CurrentAlphaRange(AxesModel axes)
    {
        if (axes.AlphaLimits is { } pinned)
        {
            return pinned;
        }

        foreach (PlotObject plot in axes.Plots)
        {
            double[,]? data = plot switch
            {
                SurfacePlot surface => surface.AlphaData,
                ImagePlot image => image.AlphaData,
                _ => null,
            };

            if (data is not null)
            {
                double min = double.PositiveInfinity, max = double.NegativeInfinity;
                foreach (double alpha in data)
                {
                    if (double.IsFinite(alpha))
                    {
                        min = System.Math.Min(min, alpha);
                        max = System.Math.Max(max, alpha);
                    }
                }

                if (double.IsFinite(min) && double.IsFinite(max) && max > min)
                {
                    return new DataRange(min, max);
                }
            }
        }

        return new DataRange(0, 1);
    }

    // --- Clipping, face order, and the pointer --------------------------------------------------

    private static void AddClippingBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Clipping",
            entry => OnOff(Axes(entry).Clipping),
            (entry, value, line, col) => Axes(entry).Clipping = ToOnOff("Clipping", value, line, col));

        Put(table, "ClippingStyle",
            entry => JgsValue.Str("rectangle"),
            (entry, value, line, col) =>
            {
                // The renderer clips to the plot rectangle and has no primitive for the six planes of
                // the box, so '3dbox' is refused rather than accepted and forgotten. That refusal is
                // a recorded divergence: MATLAB defaults to '3dbox'.
                string word = JgsBuiltins.StrOf("ClippingStyle", value, line, col).ToLowerInvariant();
                if (word != "rectangle")
                {
                    throw new JgsRuntimeException(line, col,
                        $"ClippingStyle is 'rectangle' here: content is clipped to the plot rectangle, "
                        + $"and clipping against the six planes of the plot box ('{word}') is not implemented.");
                }
            });

        Put(table, "SortMethod",
            entry => JgsValue.Str(
                Axes(entry).SortMethod == SortMethodType.ChildOrder ? "childorder" : "depth"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("SortMethod", value, line, col).ToLowerInvariant();
                Axes(entry).SortMethod = word switch
                {
                    "depth" => SortMethodType.Depth,
                    "childorder" => SortMethodType.ChildOrder,
                    _ => throw new JgsRuntimeException(line, col,
                        $"SortMethod is 'depth' or 'childorder', but got '{word}'."),
                };
            });

        // Where the pointer last crossed, as the two ends of the line of sight through the axes.
        // Read-only: it is something the pointer did, not something a script decides.
        Put(table, "CurrentPoint",
            entry =>
            {
                (Vector3D front, Vector3D back) = Axes(entry).CurrentPoint;
                return JgsMatrix.FromColumnMajor(
                    [front.X, back.X, front.Y, back.Y, front.Z, back.Z], 2, 3);
            });
    }

    // --- Alpha data on the two plots that can draw it -------------------------------------------

    /// <summary>
    /// A surface's own transparency: the grid of alpha data, and the word that says whether the
    /// faces take their alpha from it. MATLAB spells the second one as the FaceAlpha property
    /// holding <c>'flat'</c> instead of a number, so that is how it reads and writes here.
    /// </summary>
    private static void AddSurfaceAlphaData(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "AlphaData",
            entry => ((SurfacePlot)entry.Target).AlphaData is { } data
                ? Grid(data)
                : JgsValue.Number(1),
            (entry, value, line, col) =>
            {
                var surface = (SurfacePlot)entry.Target;
                double[,]? grid = AlphaGrid("AlphaData", value, line, col);
                try
                {
                    surface.AlphaData = grid;
                }
                catch (ArgumentException mismatch)
                {
                    // A grid of the wrong shape is a mistake in the script, so it is reported as one
                    // rather than thrown out of the engine as a bare exception.
                    throw new JgsRuntimeException(line, col, mismatch.Message);
                }

                surface.FaceAlphaFlat = surface.AlphaData is not null;
            });

        Put(table, "FaceAlpha",
            entry =>
            {
                var surface = (SurfacePlot)entry.Target;
                return surface.FaceAlphaFlat ? JgsValue.Str("flat") : JgsValue.Number(surface.FaceAlpha);
            },
            (entry, value, line, col) =>
            {
                var surface = (SurfacePlot)entry.Target;
                if (value.Type == JgsType.String)
                {
                    string word = JgsBuiltins.StrOf("FaceAlpha", value, line, col).ToLowerInvariant();
                    switch (word)
                    {
                        case "flat":
                            if (surface.AlphaData is null)
                            {
                                throw new JgsRuntimeException(line, col,
                                    "FaceAlpha 'flat' takes its transparency from AlphaData, which this "
                                    + "surface has not been given.");
                            }

                            // The word replaces the number rather than multiplying with it, so the
                            // scalar goes back to opaque and the map alone decides.
                            surface.FaceAlpha = 1;
                            surface.FaceAlphaFlat = true;
                            return;
                        case "interp":
                            throw new JgsRuntimeException(line, col,
                                "FaceAlpha 'interp' shades the alpha across each face, which is not "
                                + "implemented; 'flat' gives each face its own transparency.");
                        default:
                            throw new JgsRuntimeException(line, col,
                                $"FaceAlpha is a number between 0 and 1 or the word 'flat', but got '{word}'.");
                    }
                }

                surface.FaceAlphaFlat = false;
                surface.FaceAlpha = Numbers("FaceAlpha", value, 1, line, col)[0];
            });
    }

    /// <summary>An image's per-pixel transparency, looked up in the axes' alphamap.</summary>
    private static void AddImageAlphaData(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "AlphaData",
            entry => ((ImagePlot)entry.Target).AlphaData is { } data
                ? Grid(data)
                : JgsValue.Number(1),
            (entry, value, line, col) =>
            {
                double[,]? grid = AlphaGrid("AlphaData", value, line, col);
                try
                {
                    ((ImagePlot)entry.Target).AlphaData = grid;
                }
                catch (ArgumentException mismatch)
                {
                    throw new JgsRuntimeException(line, col, mismatch.Message);
                }
            });
    }

    /// <summary>
    /// Alpha data as the model holds it: a grid of transparencies, or null when the script writes the
    /// scalar 1, which is MATLAB's way of saying the object is uniformly opaque again.
    /// </summary>
    private static double[,]? AlphaGrid(string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            double scalar = value.AsNumber;
            if (scalar == 1)
            {
                return null;
            }

            throw new JgsRuntimeException(line, col,
                $"{name} is a grid of transparencies, one per point; the only scalar it takes is 1, "
                + "which means no alpha data at all.");
        }

        double[][] rows = JgsMatrix.ToRows(name, value, line, col);
        int columns = rows.Length == 0 ? 0 : rows[0].Length;
        var grid = new double[rows.Length, columns];
        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r].Length != columns)
            {
                throw new JgsRuntimeException(line, col, $"{name} rows must all be the same length.");
            }

            for (int c = 0; c < columns; c++)
            {
                grid[r, c] = rows[r][c];
            }
        }

        return grid;
    }
}
