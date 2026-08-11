using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>
    /// M54 wave F: the legacy appearance verbs — the colormap generators that are palettes rather
    /// than gradients, the two reflectance functions a hand-written shading model needs, and the
    /// half-dozen commands that predate figure properties and set a look by name.
    /// <para>
    /// <c>flag</c> and <c>prism</c> are not here: they are ordinary colormaps and joined the
    /// generator table beside <c>jet</c>, which is what makes <c>colormap('flag')</c> and
    /// <c>flag(8)</c> the same map without a second definition to keep in step.
    /// </para>
    /// </summary>
    private static void RegisterLegacyAppearanceBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("colorcube", (args, line, col) =>
        {
            ArityRange("colorcube", args, 0, 1, line, col);
            int rows = args.Count == 1 ? Count("colorcube", args, 0, line, col) : DefaultColormapRows;
            if (rows < 1)
            {
                throw new JgsRuntimeException(line, col, "colorcube: the row count must be at least 1.");
            }

            return MatrixFromRows(ColorCube(rows));
        });

        Define("rgbplot", (args, line, col) => RgbPlot(args, line, col));
        DefineBare("validatecolor", (args, line, col) => ValidateColor(args, line, col));
        Define("diffuse", (args, line, col) => Diffuse(args, line, col));
        Define("specular", (args, line, col) => Specular(args, line, col));
        DefineBare("contrast", (args, line, col) => Contrast(args, line, col));
        Define("hidden", (args, line, col) => Hidden(args, line, col, dialect));
        DefineBare("orient", (args, line, col) => Orient(args, line, col));
        Define("whitebg", (args, line, col) => WhiteBg(args, line, col));
        Define("colordef", (args, line, col) => ColorDef(args, line, col));

        // Accepted and does nothing: there is no renderer to select. Refusing it would fail scripts
        // that ask for hardware drawing out of habit, and reporting a mode would be a lie.
        Define("opengl", (args, line, col) =>
        {
            ArityRange("opengl", args, 0, 2, line, col);
            return JgsValue.Null;
        });
    }

    /// <summary>
    /// The <c>colorcube</c> table: as many evenly spaced colours of the RGB cube as fit, then the
    /// pure red, green and blue ramps, then a grey ramp, then black.
    /// </summary>
    /// <remarks>
    /// A construction to the documented description rather than a copy of MATLAB's row set — the
    /// cube's step count and how the remainder is split between ramps are choices, and MATLAB's are
    /// not published. The shape of the answer is the same: an even sweep of the cube with the axes
    /// and the grey line reinforced at the end, and black last.
    /// </remarks>
    private static double[][] ColorCube(int rows)
    {
        var map = new List<double[]>(rows);

        // The largest cube whose off-grey corners fit, leaving at least a quarter of the rows for
        // the ramps — which is what stops a small request from being all cube and no grey.
        int steps = 1;
        while (Cube(steps + 1) <= rows * 3 / 4)
        {
            steps++;
        }

        for (int r = 0; r < steps && map.Count < rows; r++)
        {
            for (int g = 0; g < steps && map.Count < rows; g++)
            {
                for (int b = 0; b < steps && map.Count < rows; b++)
                {
                    // The grey line comes back at the end with more steps than the cube can give it.
                    if (r == g && g == b)
                    {
                        continue;
                    }

                    map.Add([Level(r, steps), Level(g, steps), Level(b, steps)]);
                }
            }
        }

        // Whatever is left, split four ways: the three pure axes, then grey. Each ramp descends from
        // full toward black without reaching it, so black can be the single last row.
        int remaining = System.Math.Max(rows - map.Count - 1, 0);
        int each = remaining / 4;
        Ramp(map, each, (v, ramp) => ramp switch
        {
            0 => [v, 0, 0],
            1 => [0, v, 0],
            2 => [0, 0, v],
            _ => [v, v, v],
        });

        while (map.Count < rows - 1)
        {
            map.Add([0.5, 0.5, 0.5]);
        }

        if (map.Count < rows)
        {
            map.Add([0, 0, 0]);
        }

        return [.. map];

        static int Cube(int n) => n * n * n;
        static double Level(int i, int n) => n == 1 ? 1 : i / (double)(n - 1);

        static void Ramp(List<double[]> into, int each, Func<double, int, double[]> row)
        {
            for (int ramp = 0; ramp < 4; ramp++)
            {
                for (int i = 0; i < each; i++)
                {
                    into.Add(row((each - i) / (double)(each + 1), ramp));
                }
            }
        }
    }

    /// <summary><c>rgbplot(map)</c>: the three columns of a colormap against the row number.</summary>
    private static JgsValue RgbPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("rgbplot", args, 1, line, col);
        double[][] map = JgsMatrix.ToRows("rgbplot", args[0], line, col);
        if (map.Length == 0 || map[0].Length != 3)
        {
            throw new JgsRuntimeException(line, col, "rgbplot: a colormap has three columns (R, G, B).");
        }

        int rows = map.Length;
        var index = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            index[i] = i + 1;
        }

        double[] Channel(int channel)
        {
            var values = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                values[i] = map[i][channel];
            }

            return values;
        }

        // The first call replaces whatever was in the axes; the other two are added beside it, so
        // rgbplot leaves three lines however the axes was left.
        JG.Plot(index, Channel(0)).Color = Colors.Red;
        AxesModel axes = JG.Gca();
        axes.AddLine(index, Channel(1)).Color = Colors.Green;
        axes.AddLine(index, Channel(2)).Color = Colors.Blue;
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>validatecolor</c>: whatever a colour was written as, back as rows of RGB in [0, 1].
    /// <c>'one'</c> (the default) insists on a single colour; <c>'multiple'</c> takes a list.
    /// </summary>
    private static JgsValue ValidateColor(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("validatecolor", args, 1, 2, line, col);
        bool many = false;
        if (args.Count == 2)
        {
            string word = Str("validatecolor", args, 1, line, col).ToLowerInvariant();
            many = word switch
            {
                "multiple" => true,
                "one" => false,
                _ => throw new JgsRuntimeException(line, col,
                    $"validatecolor: the second argument is 'one' or 'multiple', but got '{word}'."),
            };
        }

        List<Color> colors = ColorList(args[0], line, col);
        if (!many && colors.Count != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"validatecolor: expected one colour, but got {colors.Count}. Pass 'multiple' to allow a list.");
        }

        var rows = new double[colors.Count][];
        for (int i = 0; i < colors.Count; i++)
        {
            rows[i] = [colors[i].R / 255.0, colors[i].G / 255.0, colors[i].B / 255.0];
        }

        return MatrixFromRows(rows);
    }

    /// <summary>Every colour in one argument: a name, a hex string, a cell of either, or a table of triplets.</summary>
    private static List<Color> ColorList(JgsValue value, int line, int col)
    {
        var colors = new List<Color>();
        if (value.Type == JgsType.String)
        {
            colors.Add(OptionColor(value, line, col, "validatecolor"));
            return colors;
        }

        if (value.Type == JgsType.Cell)
        {
            foreach (JgsValue item in value.AsCell)
            {
                colors.AddRange(ColorList(item, line, col));
            }

            return colors;
        }

        double[][] table = JgsMatrix.ToRows("validatecolor", value, line, col);
        foreach (double[] row in table)
        {
            if (row.Length != 3)
            {
                throw new JgsRuntimeException(line, col,
                    "validatecolor: a numeric colour is an [r g b] triplet, or a table of them with three columns.");
            }

            foreach (double component in row)
            {
                if (!(component >= 0 && component <= 1))
                {
                    throw new JgsRuntimeException(line, col,
                        $"validatecolor: components are in [0, 1], but one is {component:G6}.");
                }
            }

            colors.Add(Color.FromScRgb(row[0], row[1], row[2]));
        }

        return colors;
    }

    /// <summary>
    /// <c>diffuse(Nx, Ny, Nz, S)</c>: how much of a light at <c>S</c> a surface with those normals
    /// reflects — the cosine of the angle between them, and nothing where the light is behind.
    /// </summary>
    private static JgsValue Diffuse(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("diffuse", args, 4, line, col);
        (double[] nx, double[] ny, double[] nz) = Normals("diffuse", args, line, col);
        (double sx, double sy, double sz) = Direction("diffuse", args[3], line, col);

        var answer = new double[nx.Length];
        for (int i = 0; i < answer.Length; i++)
        {
            answer[i] = System.Math.Max(0, Dot(nx[i], ny[i], nz[i], sx, sy, sz));
        }

        return Reflectance(args[0], answer);
    }

    /// <summary>
    /// <c>specular(Nx, Ny, Nz, S, V, spread)</c>: the highlight a light at <c>S</c> throws toward a
    /// viewer at <c>V</c>. The reflected ray never has to be built — for unit vectors its cosine
    /// against the viewer is <c>2(N·S)(N·V) − (S·V)</c>, which is the whole model.
    /// </summary>
    private static JgsValue Specular(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("specular", args, 5, 6, line, col);
        (double[] nx, double[] ny, double[] nz) = Normals("specular", args, line, col);
        (double sx, double sy, double sz) = Direction("specular", args[3], line, col);
        (double vx, double vy, double vz) = Direction("specular", args[4], line, col);
        double spread = args.Count == 6 ? Num("specular", args, 5, line, col) : DefaultSpecularSpread;
        if (!(spread > 0))
        {
            throw new JgsRuntimeException(line, col, "specular: the spread exponent must be positive.");
        }

        double sv = (sx * vx) + (sy * vy) + (sz * vz);
        var answer = new double[nx.Length];
        for (int i = 0; i < answer.Length; i++)
        {
            double reflected = (2 * Dot(nx[i], ny[i], nz[i], sx, sy, sz) * Dot(nx[i], ny[i], nz[i], vx, vy, vz)) - sv;
            answer[i] = reflected <= 0 ? 0 : System.Math.Pow(reflected, spread);
        }

        return Reflectance(args[0], answer);
    }

    /// <summary>
    /// The answer in the shape the normals arrived in — and a plain number for one normal, since a
    /// scalar in has to be a scalar out for <c>diffuse(0, 0, 1, S) * base</c> to mean anything.
    /// </summary>
    private static JgsValue Reflectance(JgsValue source, double[] answer) =>
        answer.Length == 1 && source.Type is JgsType.Number or JgsType.Bool
            ? JgsValue.Number(answer[0])
            : JgsMatrix.Like(source, Numbers(answer));

    /// <summary>MATLAB's default specular spread: a tight highlight, but not a point.</summary>
    private const double DefaultSpecularSpread = 10;

    /// <summary>The three normal components, checked for a common length.</summary>
    private static (double[] X, double[] Y, double[] Z) Normals(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] nx = ToDoubles($"{verb}: Nx", args[0], line, col);
        double[] ny = ToDoubles($"{verb}: Ny", args[1], line, col);
        double[] nz = ToDoubles($"{verb}: Nz", args[2], line, col);
        if (ny.Length != nx.Length || nz.Length != nx.Length)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: Nx, Ny and Nz must be the same size.");
        }

        return (nx, ny, nz);
    }

    /// <summary>
    /// A direction given as <c>[x y z]</c> or, MATLAB's other form, as <c>[azimuth elevation]</c> in
    /// degrees. Returned as a unit vector, so the cosines below need no lengths.
    /// </summary>
    private static (double X, double Y, double Z) Direction(string verb, JgsValue value, int line, int col)
    {
        double[] v = ToDoubles($"{verb}: the direction", value, line, col);
        if (v.Length == 2)
        {
            double az = v[0] * System.Math.PI / 180.0;
            double el = v[1] * System.Math.PI / 180.0;
            return (System.Math.Cos(el) * System.Math.Cos(az), System.Math.Cos(el) * System.Math.Sin(az), System.Math.Sin(el));
        }

        if (v.Length != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: a direction is [x y z] or [azimuth elevation] in degrees.");
        }

        double length = System.Math.Sqrt((v[0] * v[0]) + (v[1] * v[1]) + (v[2] * v[2]));
        return length < 1e-300 ? (0, 0, 1) : (v[0] / length, v[1] / length, v[2] / length);
    }

    /// <summary>The cosine between a normal and a unit direction, the normal normalized as it goes.</summary>
    private static double Dot(double nx, double ny, double nz, double x, double y, double z)
    {
        double length = System.Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return length < 1e-300 ? 0 : (((nx * x) + (ny * y) + (nz * z)) / length);
    }

    /// <summary>
    /// <c>contrast(X)</c>: the grey colormap that spreads a picture's own histogram evenly over the
    /// display range, so <c>colormap(contrast(X))</c> equalizes it without touching the data.
    /// </summary>
    private static JgsValue Contrast(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("contrast", args, 1, 2, line, col);
        double[] values = ToDoubles("contrast", args[0], line, col);
        int rows = args.Count == 2 ? Count("contrast", args, 1, line, col) : DefaultContrastRows;
        if (rows < 1)
        {
            throw new JgsRuntimeException(line, col, "contrast: the row count must be at least 1.");
        }

        double[] sorted = [.. values.Where(double.IsFinite).Order()];
        if (sorted.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "contrast: the data has no finite values to equalize.");
        }

        double low = sorted[0], high = sorted[^1];
        var map = new double[rows][];
        for (int i = 0; i < rows; i++)
        {
            // Row i is shown for data around the middle of its own band, so its grey is the share of
            // the data at or below that value — which is exactly a flattened histogram.
            double value = rows == 1 ? low : low + ((i + 0.5) / rows * (high - low));
            double grey = Share(sorted, value);
            map[i] = [grey, grey, grey];
        }

        return MatrixFromRows(map);

        static double Share(double[] sorted, double value)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid] <= value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo / (double)sorted.Length;
        }
    }

    /// <summary>How many grey levels <c>contrast</c> answers with when it is not told — MATLAB's default.</summary>
    private const int DefaultContrastRows = 64;

    /// <summary>
    /// <c>hidden on</c> / <c>hidden off</c>: whether a mesh hides what is behind it. A mesh here is
    /// a wireframe with no faces at all, so hiding is done by painting its faces the axes' own
    /// background — the same picture, and reversible.
    /// </summary>
    private static JgsValue Hidden(IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("hidden", rest, 0, 1, line, col);
        AxesModel axes = named ?? JG.Gca();

        bool on = OnOff("hidden", rest, line, col, dialect, () => axes.Plots.OfType<SurfacePlot>().Any(s => s.FaceColor is not null));
        foreach (SurfacePlot surface in axes.Plots.OfType<SurfacePlot>())
        {
            if (on && surface.Style == SurfaceStyle.Wireframe)
            {
                surface.Style = SurfaceStyle.FilledWithWireframe;
                surface.FaceColor = axes.Background;
            }
            else if (!on && surface.FaceColor is not null)
            {
                surface.Style = SurfaceStyle.Wireframe;
                surface.FaceColor = null;
            }
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>orient</c>: the paper orientation a figure would print at. Answers <c>'portrait'</c> and
    /// accepts the other two words without effect — export here is sized by the figure, not by a
    /// page, so there is no orientation for a word to change.
    /// </summary>
    private static JgsValue Orient(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? _, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("orient", rest, 0, 1, line, col);
        if (rest.Count == 0)
        {
            return JgsValue.Str("portrait");
        }

        string word = Str("orient", rest, 0, line, col).ToLowerInvariant();
        if (word is not ("portrait" or "landscape" or "tall"))
        {
            throw new JgsRuntimeException(line, col,
                $"orient: the orientation is 'portrait', 'landscape' or 'tall', but got '{word}'.");
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>whitebg</c>: swap the figure to a background colour and move the ink to suit it. With no
    /// colour it toggles between light and dark, which is what the name has always meant.
    /// </summary>
    private static JgsValue WhiteBg(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("whitebg", args, 0, 2, line, col);
        IReadOnlyList<JgsValue> rest = args;
        FigureModel figure = JG.CurrentFigure;

        // whitebg(fig, c): a leading figure handle is a figure number, since that is what a figure
        // handle is here.
        if (args.Count == 2)
        {
            int number = Count("whitebg", args, 0, line, col);
            if (!JG.TryGetFigure(number, out FigureModel? named))
            {
                throw new JgsRuntimeException(line, col, $"whitebg: there is no figure {number}.");
            }

            figure = named;
            rest = [args[1]];
        }

        Color background = rest.Count == 0
            ? (IsDark(figure.Background) ? Colors.White : Colors.Black)
            : OptionColor(rest[0], line, col, "whitebg");

        ContrastingTheme(background).Apply(figure);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>colordef white</c> / <c>black</c> / <c>none</c>: the two whole looks <c>whitebg</c>
    /// toggles between, by name. <c>none</c> is the light one, which is what a figure starts as.
    /// </summary>
    private static JgsValue ColorDef(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("colordef", args, 1, 2, line, col);
        FigureModel figure = JG.CurrentFigure;
        int at = 0;
        if (args.Count == 2)
        {
            int number = Count("colordef", args, 0, line, col);
            if (!JG.TryGetFigure(number, out FigureModel? named))
            {
                throw new JgsRuntimeException(line, col, $"colordef: there is no figure {number}.");
            }

            figure = named;
            at = 1;
        }

        string word = Str("colordef", args, at, line, col).ToLowerInvariant();
        ITheme theme = word switch
        {
            "white" or "none" => Theme.Light,
            "black" => Theme.Dark,
            _ => throw new JgsRuntimeException(line, col,
                $"colordef: the choice is 'white', 'black' or 'none', but got '{word}'."),
        };

        theme.Apply(figure);
        return JgsValue.Null;
    }

    /// <summary>Whether a background wants light ink on it — the usual luminance test.</summary>
    private static bool IsDark(Color color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) < 128;

    /// <summary>
    /// A theme that puts a figure on one background colour with ink that can be read against it.
    /// Built from the light or dark preset so the typography and the series palette come from a real
    /// theme rather than being invented here.
    /// </summary>
    private static Theme ContrastingTheme(Color background)
    {
        Theme baseline = IsDark(background) ? Theme.Dark : Theme.Light;
        return new Theme
        {
            Name = "whitebg",
            FigureBackground = background,
            AxesBackground = background,
            AxisLine = baseline.AxisLine,
            TickLabel = baseline.TickLabel,
            AxisLabel = baseline.AxisLabel,
            Title = baseline.Title,
            MajorGrid = baseline.MajorGrid,
            MinorGrid = baseline.MinorGrid,
            FontFamily = baseline.FontFamily,
            FigureTitleFontSize = baseline.FigureTitleFontSize,
            AxesTitleFontSize = baseline.AxesTitleFontSize,
            AxisLabelFontSize = baseline.AxisLabelFontSize,
            TickLabelFontSize = baseline.TickLabelFontSize,
            BoldTitles = baseline.BoldTitles,
            SeriesPalette = baseline.SeriesPalette,
        };
    }
}
