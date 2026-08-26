using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M60 waves D and E: the verbs that move, and the verbs that would wait for a mouse.
/// <para>
/// Every animated verb here draws its finished picture first and then, if there is a player to show
/// it with, replays how it got there. That order is the whole design: the drawing is what a saved
/// figure holds and what a headless run is asked for, and the motion is something a window adds. A
/// script that runs under <c>-batch</c> therefore gets a real answer from <c>comet</c> rather than
/// nothing, and the stress gate can check it.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The three of MATLAB's five interactivity words that name machinery this build does not have:
    /// a legacy exploration mode to restore, and a toolbar button to add or take away. The other two
    /// — <c>disableDefaultInteractivity</c> and its opposite — became real in M80, when an axes got a
    /// list of gestures for them to turn off.
    /// </summary>
    private static readonly string[] InteractivityToggles =
    [
        "enableLegacyExplorationModes",
        "addToolbarExplorationButtons", "removeToolbarExplorationButtons",
    ];

    private static void RegisterMotionBuiltins(JgsEnvironment env)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // --- Motion -------------------------------------------------------------------------------
        DefineSilent("comet", (args, line, col) => Comet("comet", args, line, col, spatial: false));
        DefineSilent("comet3", (args, line, col) => Comet("comet3", args, line, col, spatial: true));
        DefineSilent("movie", Movie);
        DefineSilent("streamparticles", StreamParticles);
        Define("interpstreamspeed", InterpStreamSpeed);

        // --- Modes ---------------------------------------------------------------------------------
        DefineSilent("pan", (args, line, col) => Mode("pan", args, line, col));
        DefineSilent("datacursormode", (args, line, col) => Mode("datacursormode", args, line, col));
        DefineSilent("gtext", GText);
        DefineSilent("rotate", Rotate);

        // Accepted and recorded: JGraph's window has no legacy exploration mode to restore and no
        // toolbar button to add, so these three change nothing. Answering rather than refusing is the
        // point — a script that begins by turning an exploration mode off should still run.
        foreach (string toggle in InteractivityToggles)
        {
            DefineSilent(toggle, (_, _, _) => JgsValue.Null);
        }

        // These two do change something since M80: an axes keeps the list of gestures it answers to,
        // and these are the switch over the whole of it.
        DefineSilent("disableDefaultInteractivity",
            (args, line, col) => DefaultInteractivity(args, line, col, on: false));
        DefineSilent("enableDefaultInteractivity",
            (args, line, col) => DefaultInteractivity(args, line, col, on: true));

        RegisterInteractionBuiltins(env);
        RegisterToolbarBuiltins(env);
    }

    // --- comet and comet3 --------------------------------------------------------------------------

    private static JgsValue Comet(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool spatial)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            // The trailing p is the fraction of the curve the comet's tail is long. It changes what
            // the motion looks like and nothing about the finished line, so it is read and kept for
            // the player rather than applied to the drawing.
            double tail = 0.1;
            var coordinates = new List<double[]>();
            foreach (JgsValue arg in rest)
            {
                coordinates.Add(ToDoubles(verb, arg, line, col));
            }

            int wanted = spatial ? 3 : 2;
            if (coordinates.Count == wanted + 1)
            {
                double[] last = coordinates[^1];
                if (last.Length == 1)
                {
                    tail = System.Math.Clamp(last[0], 0, 1);
                    coordinates.RemoveAt(coordinates.Count - 1);
                }
            }

            if (coordinates.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{verb} expects the points to travel along.");
            }

            // comet(y) counts along the whole numbers, exactly as plot(y) does.
            if (coordinates.Count == 1 && !spatial)
            {
                coordinates.Insert(0, [.. Enumerable.Range(1, coordinates[0].Length).Select(i => (double)i)]);
            }

            if (coordinates.Count != wanted)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} takes {wanted} coordinate vectors" + (spatial ? "." : ", or one."));
            }

            int count = coordinates.Min(c => c.Length);
            if (count < 2)
            {
                throw new JgsRuntimeException(line, col, $"{verb} needs at least two points to travel between.");
            }

            PlotObject drawn = spatial
                ? JG.Plot3(coordinates[0], coordinates[1], coordinates[2])
                : JG.Plot(coordinates[0], coordinates[1]);

            Animate(drawn, coordinates, count, tail, spatial);
            return JgsValue.Null;
        });
    }

    /// <summary>
    /// Replays a comet's travel, if anything can show it. The steps rewrite the line's own data, so
    /// the last step leaves exactly the whole curve that was drawn before the replay began — which
    /// is what makes a cancelled or unplayed animation indistinguishable from a finished one.
    /// </summary>
    private static void Animate(
        PlotObject drawn, List<double[]> coordinates, int count, double tail, bool spatial)
    {
        if (!ScriptAnimation.HasPlayer)
        {
            return;
        }

        int trail = System.Math.Max(2, (int)System.Math.Round(count * tail));
        var steps = new List<Action>(count);
        for (int step = 2; step <= count; step++)
        {
            int upTo = step;
            int from = System.Math.Max(0, upTo - trail);
            steps.Add(() =>
            {
                double[] Segment(int index) => coordinates[index][from..upTo];
                if (spatial && drawn is Line3DPlot spatialLine)
                {
                    spatialLine.SetData(Segment(0), Segment(1), Segment(2));
                }
                else if (drawn is LinePlot flat)
                {
                    flat.SetData(Segment(0), Segment(1));
                }
            });
        }

        // Whatever the replay did, the finished curve is what stays behind.
        steps.Add(() =>
        {
            if (spatial && drawn is Line3DPlot spatialLine)
            {
                spatialLine.SetData(coordinates[0], coordinates[1], coordinates[2]);
            }
            else if (drawn is LinePlot flat)
            {
                flat.SetData(coordinates[0], coordinates[1]);
            }
        });

        ScriptAnimation.Play(JG.CurrentFigure, steps, 1.0 / 60);
    }

    // --- movie ------------------------------------------------------------------------------------

    private static JgsValue Movie(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        if (rest.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "movie expects the frames to play, as getframe returns them.");
        }

        // The frames are decoded whether or not anything will play them, so a script that hands
        // movie the wrong thing hears about it in batch rather than only in a window.
        (List<uint[]> frames, int width, int height) = FramePixels("movie", rest[0], line, col);
        int repeats = rest.Count > 1 ? (int)ScalarOf("movie", rest[1], line, col) : 1;
        double rate = rest.Count > 2 ? ScalarOf("movie", rest[2], line, col) : 12;
        if (rate <= 0)
        {
            throw new JgsRuntimeException(line, col, "movie: the frame rate has to be a positive number.");
        }

        // The same order every animated verb here uses: draw the finished picture, then replay how
        // it got there. A movie's finished picture is its last frame, which is what MATLAB leaves
        // behind too — so a batch run gets a real answer from movie rather than an empty figure.
        int number = JG.GetFigureNumber(figure);
        if (number >= 1)
        {
            JG.Figure(number);
        }

        JG.Clf();
        RgbImagePlot screen = JG.RgbImage(frames[^1], width, height);
        StyleImageAxes(JG.Gca());

        if (ScriptAnimation.HasPlayer && frames.Count > 1)
        {
            var steps = new List<Action>();
            for (int repeat = 0; repeat < System.Math.Max(1, System.Math.Abs(repeats)); repeat++)
            {
                foreach (uint[] frame in frames)
                {
                    steps.Add(() => screen.SetPixels(frame));
                }
            }

            ScriptAnimation.Play(figure, steps, 1.0 / rate);

            // Whatever the replay did — including being cut short — the last frame is what stays.
            screen.SetPixels(frames[^1]);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// The pictures a value holds, as row-major ARGB, refusing anything that is not a frame or a row
    /// of them. Every frame has to be the same size, because they share one image plot and a script
    /// that captured two different figures did not mean to make a movie of them.
    /// </summary>
    private static (List<uint[]> Frames, int Width, int Height) FramePixels(
        string verb, JgsValue value, int line, int col)
    {
        var structs = new List<JgsValue>();
        if (value.Type == JgsType.Struct)
        {
            // [f g] is one struct array of two frames, not two values — so the frames of a movie
            // arrive under a single Struct and have to be walked. Before struct arrays were real this
            // read as one frame and quietly played the first of them.
            foreach (Dictionary<string, JgsValue> element in value.AsStructArray.Elements)
            {
                structs.Add(JgsValue.Struct(element));
            }
        }
        else if (value.Type is JgsType.Array or JgsType.Cell)
        {
            for (int i = 0; i < value.ArrayLength; i++)
            {
                if (value.ElementAt(i).Type == JgsType.Struct)
                {
                    structs.Add(value.ElementAt(i));
                }
            }
        }

        if (structs.Count == 0 || structs.Any(s => !HasField(s, "cdata")))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: a frame is what getframe answers with — a struct holding cdata.");
        }

        var frames = new List<uint[]>(structs.Count);
        int width = 0;
        int height = 0;
        foreach (JgsValue frame in structs)
        {
            (uint[] pixels, int w, int h) = FrameArgb(verb, frame.AsStruct["cdata"], line, col);
            if (frames.Count == 0)
            {
                (width, height) = (w, h);
            }
            else if (w != width || h != height)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: frame {frames.Count + 1} is {h}-by-{w} and the first is {height}-by-{width}; "
                    + "every frame of one movie has to be the same size.");
            }

            frames.Add(pixels);
        }

        return (frames, width, height);
    }

    /// <summary>
    /// One frame's <c>cdata</c> — a height-by-width-by-3 array of bytes, column-major — as the
    /// row-major ARGB an image plot draws. This is the transpose <c>getframe</c> did on the way out,
    /// undone.
    /// </summary>
    private static (uint[] Pixels, int Width, int Height) FrameArgb(
        string verb, JgsValue cdata, int line, int col)
    {
        IReadOnlyList<int> dims = JgsMatrix.DimsOf(cdata);
        if (dims.Count != 3 || dims[2] != 3 || dims[0] < 1 || dims[1] < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: a frame's cdata is a height-by-width-by-3 array of colours.");
        }

        int height = dims[0];
        int width = dims[1];
        double[] flat = ToDoubles(verb, cdata, line, col);
        int plane = height * width;
        var pixels = new uint[plane];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int at = (c * height) + r;
                pixels[(r * width) + c] = 0xFF000000u
                    | (Level(flat[at]) << 16)
                    | (Level(flat[at + plane]) << 8)
                    | Level(flat[at + (2 * plane)]);
            }
        }

        return (pixels, width, height);

        static uint Level(double value) =>
            (uint)System.Math.Clamp((int)System.Math.Round(value), 0, 255);
    }

    private static bool HasField(JgsValue value, string name) =>
        value.Type == JgsType.Struct && value.AsStruct.ContainsKey(name);

    // --- The two streamline verbs the volume milestone left for the animation seam -----------------

    private static JgsValue StreamParticles(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "streamparticles expects the traced lines stream2 or stream3 answered with.");
        }

        List<List<(double X, double Y, double Z)>> lines =
            ReadTracedLines("streamparticles", args[0], line, col);

        var spec = new OptionSpec(
            "streamparticles",
            [],
            ["ParticleAlignment", "FrameRate", "Animate", "MarkerSize", "MarkerFaceColor", "MarkerEdgeColor"],
            AllowNumericFlag: true);
        ParsedArgs options = spec.Parse([.. args.Skip(1)], 0, line, col);

        // MATLAB's n is the fraction of a line's points that carry a particle, and it arrives as a
        // bare number rather than under a name.
        double density = options.NumericFlag is { } given ? ScalarOf("streamparticles", given, line, col) : 0.2;
        density = density > 1 ? 1.0 / density : System.Math.Clamp(density, 1e-3, 1);

        // Which point of its own line each particle sits on, as a fraction of the way along. The
        // animation slides every particle by the same fraction, so the whole cloud drifts downstream
        // together rather than each particle running its own line at its own rate.
        var seats = new List<(List<(double X, double Y, double Z)> Line, double At)>();
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count == 0)
            {
                continue;
            }

            // The particles are spread evenly along each line rather than dropped at random, so the
            // same call twice draws the same picture — which a stress script depends on and an
            // animation does not care about either way.
            int wanted = System.Math.Max(1, (int)System.Math.Round(points.Count * density));
            for (int i = 0; i < wanted; i++)
            {
                seats.Add((points, wanted == 1 ? 0 : (double)i / (wanted - 1)));
            }
        }

        if (seats.Count == 0)
        {
            return JgsValue.Null;
        }

        (double[] xs, double[] ys, double[] zs) = ParticlesAt(seats, 0);

        // Every particle is one marker set, so a script gets one handle rather than a cloud of them
        // — the same decision coneplot made in M59.
        Scatter3DPlot markers = JG.Scatter3(xs, ys, zs);
        if (options.Scalar("MarkerSize", double.NaN) is var size && !double.IsNaN(size))
        {
            markers.MarkerSize = System.Math.Max(1, size);
        }

        Drift(markers, seats, options, line, col);
        return JgsHandleRegistry.For(markers);
    }

    /// <summary>How many places along a line one pass of the animation stops at.</summary>
    private const int DriftSteps = 60;

    /// <summary>
    /// Slides the particles downstream, if anything can show it. <c>'Animate'</c> is how many passes
    /// to make, and its default is none: MATLAB draws a still cloud unless a script asks for motion,
    /// and so a script that never asks runs at the same speed here as it did before there was a
    /// player at all. Whatever the passes did, the still cloud is what stays behind.
    /// </summary>
    private static void Drift(
        Scatter3DPlot markers,
        List<(List<(double X, double Y, double Z)> Line, double At)> seats,
        ParsedArgs options,
        int line,
        int col)
    {
        double passes = options.Scalar("Animate", 0);
        if (!ScriptAnimation.HasPlayer || !(passes >= 1))
        {
            return;
        }

        double rate = options.Scalar("FrameRate", 12);
        if (!(rate > 0))
        {
            throw new JgsRuntimeException(line, col,
                "streamparticles: 'FrameRate' has to be a positive number of frames a second.");
        }

        var steps = new List<Action>();
        for (int pass = 0; pass < (int)passes; pass++)
        {
            for (int step = 0; step < DriftSteps; step++)
            {
                double shift = (double)step / DriftSteps;
                steps.Add(() =>
                {
                    (double[] x, double[] y, double[] z) = ParticlesAt(seats, shift);
                    markers.SetData(x, y, z);
                });
            }
        }

        steps.Add(() =>
        {
            (double[] x, double[] y, double[] z) = ParticlesAt(seats, 0);
            markers.SetData(x, y, z);
        });

        ScriptAnimation.Play(JG.CurrentFigure, steps, 1.0 / rate);
    }

    /// <summary>Where every particle is when the whole cloud has slid <paramref name="shift"/> of a line along.</summary>
    private static (double[] X, double[] Y, double[] Z) ParticlesAt(
        List<(List<(double X, double Y, double Z)> Line, double At)> seats, double shift)
    {
        var xs = new double[seats.Count];
        var ys = new double[seats.Count];
        var zs = new double[seats.Count];
        for (int i = 0; i < seats.Count; i++)
        {
            (List<(double X, double Y, double Z)> points, double at) = seats[i];

            // Past the end of its line a particle comes round to the start, which is what keeps the
            // cloud the same size for every frame instead of draining away downstream.
            double along = (at + shift) % 1.0;
            int index = (int)System.Math.Round(along * (points.Count - 1));
            (xs[i], ys[i], zs[i]) = points[System.Math.Clamp(index, 0, points.Count - 1)];
        }

        return (xs, ys, zs);
    }

    private static JgsValue InterpStreamSpeed(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "interpstreamspeed expects the traced lines to respace.");
        }

        List<List<(double X, double Y, double Z)>> lines =
            ReadTracedLines("interpstreamspeed", args[0], line, col);
        double factor = args.Count > 1 ? ScalarOf("interpstreamspeed", args[^1], line, col) : 1;
        if (!(factor > 0))
        {
            throw new JgsRuntimeException(line, col,
                "interpstreamspeed: the spacing factor has to be a positive number.");
        }

        // Respacing a line so that even steps take even times means putting its vertices at even
        // distances along it, because a particle that moves at the field's speed covers equal
        // distance in equal time only when the vertices already say so.
        // Lines traced through a plane come back with two columns and lines traced through space with
        // three, and what goes out has to match what came in — so the width is read rather than
        // guessed from whether every z happens to be zero.
        bool spatial = TracedIsSpatial("interpstreamspeed", args[0], line, col);
        var respaced = new List<JgsValue>();
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            respaced.Add(EvenlySpaced(points, factor, spatial));
        }

        return respaced.Count == 1 ? respaced[0] : JgsValue.Cell([.. respaced]);
    }

    /// <summary>Whether traced lines carry a third coordinate, read off the first one that is a table.</summary>
    private static bool TracedIsSpatial(string verb, JgsValue value, int line, int col)
    {
        for (int i = 0; i < value.ArrayLength; i++)
        {
            JgsValue entry = value.ElementAt(i);
            if (entry.Type is JgsType.Array or JgsType.Number)
            {
                return Rectangle($"{verb}: vertices", entry, line, col).GetLength(1) > 2;
            }
        }

        return false;
    }

    /// <summary>One line's vertices moved onto even spacing, at <paramref name="factor"/> times the density.</summary>
    private static JgsValue EvenlySpaced(
        List<(double X, double Y, double Z)> points, double factor, bool spatial)
    {
        if (points.Count < 2)
        {
            return VertexList(points, spatial);
        }

        var travelled = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            double dz = points[i].Z - points[i - 1].Z;
            travelled[i] = travelled[i - 1] + System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        double total = travelled[^1];
        if (total <= 0)
        {
            return VertexList(points, spatial);
        }

        int wanted = System.Math.Max(2, (int)System.Math.Round((points.Count - 1) * factor) + 1);
        var walked = new List<(double X, double Y, double Z)>(wanted);
        int cursor = 1;
        for (int i = 0; i < wanted; i++)
        {
            double at = total * i / (wanted - 1);
            while (cursor < points.Count - 1 && travelled[cursor] < at)
            {
                cursor++;
            }

            double span = travelled[cursor] - travelled[cursor - 1];
            double t = span > 0 ? (at - travelled[cursor - 1]) / span : 0;
            walked.Add((
                points[cursor - 1].X + (t * (points[cursor].X - points[cursor - 1].X)),
                points[cursor - 1].Y + (t * (points[cursor].Y - points[cursor - 1].Y)),
                points[cursor - 1].Z + (t * (points[cursor].Z - points[cursor - 1].Z))));
        }

        return VertexList(walked, spatial);
    }

    // --- The verbs that would wait for a mouse ------------------------------------------------------

    /// <summary>Which exploration mode each axes was last put into, by figure number and verb.</summary>
    /// <remarks>
    /// Process-wide and written while scripts run, so every touch of it is under its own lock: two
    /// scripts running at once in one process — which is what the test suite is — are two threads
    /// here, and an unguarded insert is what corrupts a <see cref="Dictionary{K, V}"/> for good (M94).
    /// </remarks>
    private static readonly Dictionary<string, string> Modes = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>pan</c> and <c>datacursormode</c> turn a way of using the mouse on and off. JGraph's window
    /// pans with a drag and shows a data tip from its own toolbar whatever a script says, so what
    /// these do is remember the word and answer it back — enough for a script that reads its own
    /// mode, and a recorded divergence from MATLAB, where the mode really does change the pointer.
    /// </summary>
    private static JgsValue Mode(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        string key = $"{verb}:{JG.GetFigureNumber(figure)}";

        // One lock over the read and the write, not two: a toggle has to see the word it is turning
        // over, and an insert has to be alone while it makes room.
        lock (Modes)
        {
            if (rest.Count == 0)
            {
                return JgsValue.Str(Modes.TryGetValue(key, out string? current) ? current : "off");
            }

            Modes[key] = OneOfWord(verb, rest[0], ["on", "off", "toggle"], line, col) switch
            {
                "toggle" => Modes.TryGetValue(key, out string? was) && was == "on" ? "off" : "on",
                var word => word,
            };
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>gtext</c> puts a label where the next click lands, and there is nowhere to click in a run
    /// with no window. Rather than guess a position — which would put a label somewhere the script
    /// did not ask for and never say so — it refuses, and names <c>text</c> and <c>annotation</c>,
    /// which are the two ways to say where.
    /// </summary>
    private static JgsValue GText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "gtext expects the text to place.");
        }

        _ = AnnotationString("gtext", args[0], line, col);
        throw new JgsRuntimeException(line, col,
            "gtext places a label where you click, which needs a figure window. "
            + "Use text(x, y, str) for a place in the data, or annotation('textbox', [x y w h], 'String', str) "
            + "for a place on the figure.");
    }

    /// <summary>
    /// <c>rotate</c> is not an interaction at all: it turns a plot's own data about an axis through
    /// the origin of the axes, which is arithmetic and is done here in full. The direction is given
    /// as [azimuth elevation] in degrees, or as a vector to turn about.
    /// </summary>
    private static JgsValue Rotate(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                "rotate takes the handles to turn, a direction, and the angle in degrees.");
        }

        double[] direction = ToDoubles("rotate", args[1], line, col);
        double angle = ScalarOf("rotate", args[2], line, col) * System.Math.PI / 180;

        (double X, double Y, double Z) axis = direction.Length switch
        {
            2 => Spherical(direction[0], direction[1]),
            3 => (direction[0], direction[1], direction[2]),
            _ => throw new JgsRuntimeException(line, col,
                "rotate: the direction is [azimuth elevation] in degrees or a vector [x y z]."),
        };

        double length = System.Math.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        if (length <= 0)
        {
            throw new JgsRuntimeException(line, col, "rotate: the direction cannot be all zeros.");
        }

        axis = (axis.X / length, axis.Y / length, axis.Z / length);

        // The centre is the middle of the axes' own box unless one is given, which is what makes
        // rotating a plot turn it in place rather than sweep it around the origin.
        (double X, double Y, double Z) centre = args.Count > 3
            ? RotationCentre("rotate", args[3], line, col)
            : (0, 0, 0);

        foreach (JgsValue handle in HandleList(args[0]))
        {
            JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
            RotateData(entry, axis, angle, centre);
        }

        return JgsValue.Null;
    }

    private static (double X, double Y, double Z) Spherical(double azimuthDegrees, double elevationDegrees)
    {
        double azimuth = azimuthDegrees * System.Math.PI / 180;
        double elevation = elevationDegrees * System.Math.PI / 180;
        return (
            System.Math.Cos(elevation) * System.Math.Cos(azimuth),
            System.Math.Cos(elevation) * System.Math.Sin(azimuth),
            System.Math.Sin(elevation));
    }

    private static (double X, double Y, double Z) RotationCentre(string verb, JgsValue value, int line, int col)
    {
        double[] numbers = ToDoubles(verb, value, line, col);
        return numbers.Length == 3
            ? (numbers[0], numbers[1], numbers[2])
            : throw new JgsRuntimeException(line, col, $"{verb}: the origin is a point [x y z].");
    }

    /// <summary>Turns one plot's points about an axis through a centre, by Rodrigues' formula.</summary>
    private static void RotateData(
        JgsHandleEntry entry, (double X, double Y, double Z) axis, double angle,
        (double X, double Y, double Z) centre)
    {
        if (entry.Target is not PlotObject plot)
        {
            return;
        }

        IReadOnlyDictionary<string, GraphicsProperty> table =
            JgsGraphicsProperties.TableFor(plot.GetType());
        if (!table.ContainsKey("XData") || !table.ContainsKey("YData"))
        {
            return;
        }

        double[] xs = ToDoubles("rotate", JgsGraphicsProperties.Get(entry, "XData", 0, 0), 0, 0);
        double[] ys = ToDoubles("rotate", JgsGraphicsProperties.Get(entry, "YData", 0, 0), 0, 0);
        double[] zs = table.ContainsKey("ZData")
            ? ToDoubles("rotate", JgsGraphicsProperties.Get(entry, "ZData", 0, 0), 0, 0)
            : new double[xs.Length];
        if (zs.Length != xs.Length)
        {
            zs = new double[xs.Length];
        }

        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        for (int i = 0; i < xs.Length && i < ys.Length; i++)
        {
            double x = xs[i] - centre.X;
            double y = ys[i] - centre.Y;
            double z = zs[i] - centre.Z;

            double dot = (axis.X * x) + (axis.Y * y) + (axis.Z * z);
            double crossX = (axis.Y * z) - (axis.Z * y);
            double crossY = (axis.Z * x) - (axis.X * z);
            double crossZ = (axis.X * y) - (axis.Y * x);

            xs[i] = centre.X + (x * cos) + (crossX * sin) + (axis.X * dot * (1 - cos));
            ys[i] = centre.Y + (y * cos) + (crossY * sin) + (axis.Y * dot * (1 - cos));
            zs[i] = centre.Z + (z * cos) + (crossZ * sin) + (axis.Z * dot * (1 - cos));
        }

        JgsGraphicsProperties.Set(entry, "XData", JgsMatrix.FromColumnMajor(xs, 1, xs.Length), 0, 0);
        JgsGraphicsProperties.Set(entry, "YData", JgsMatrix.FromColumnMajor(ys, 1, ys.Length), 0, 0);
        if (table.ContainsKey("ZData") && table["ZData"].Write is not null)
        {
            JgsGraphicsProperties.Set(entry, "ZData", JgsMatrix.FromColumnMajor(zs, 1, zs.Length), 0, 0);
        }
    }
}
