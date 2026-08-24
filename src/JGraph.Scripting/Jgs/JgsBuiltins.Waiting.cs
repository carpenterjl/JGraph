using JGraph.Api;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M87: the three verbs that stop and wait for a person — bare <c>pause</c>,
/// <c>waitforbuttonpress</c> and <c>ginput</c>.
/// </summary>
/// <remarks>
/// <para>
/// They wait on <see cref="ScriptInputWatch"/> rather than on the callback queue, and the difference
/// matters: that queue only ever holds an event some object has a callback for, which is what makes
/// an unscripted window cost nothing. A verb waiting for a key has to hear the key whether or not
/// anything has a <c>KeyPressFcn</c>.
/// </para>
/// <para>
/// Where there is no window they refuse by name, naming the non-interactive verb that does the job —
/// M60's answer, and M84's, and what keeps <c>jgraph.exe -batch</c> and the stress gate free of a
/// verb that would wait forever for somebody who is not there. The test for a window is
/// <see cref="ScriptEventQueue.PumpInstalled"/>, which is the same question <c>waitfor</c> already
/// asks, rather than a second seam saying the same thing.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Whether <c>pause</c> does anything at all. MATLAB's <c>pause('off')</c> makes every pause in a
    /// script return at once, which is how a demo written with pauses in it is run without them.
    /// </summary>
    private static bool _pausesEnabled = true;

    /// <summary>How long a waiting verb may block before giving up, so nothing waits forever.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromHours(1);

    private static void RegisterWaitingBuiltins(JgsEnvironment env, CancellationToken cancellationToken)
    {
        env.Declare("waitforbuttonpress", JgsValue.Function(new BuiltinFunction(
            "waitforbuttonpress",
            (args, line, col) =>
            {
                Arity("waitforbuttonpress", args, 0, line, col);
                ScriptInput input = WaitForPress(
                    "waitforbuttonpress",
                    "there is no window to press anything in — a batch run has no keyboard and no "
                    + "mouse",
                    cancellationToken,
                    line,
                    col);

                // MATLAB's answer: 0 for a mouse button, 1 for a key.
                return JgsValue.Number(input.Kind == ScriptInputKind.Key ? 1 : 0);
            })
        {
            // `w = waitforbuttonpress` with no parentheses is the documented spelling.
            AutoCallsBare = true,
        }));

        env.Declare("ginput", JgsValue.Function(new BuiltinFunction(
            "ginput",
            (args, line, col) => Ginput(args, 1, cancellationToken, line, col)[0])
        {
            BindsAnsAsStatement = false,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) =>
                Ginput(args, wanted, cancellationToken, line, col),
        }));
    }

    /// <summary>
    /// <c>pause</c> in all four of its spellings. The numeric form is M28's and unchanged; the other
    /// three are M87's, and only the bare one needs a window — <c>'on'</c>, <c>'off'</c> and
    /// <c>'query'</c> are a switch a headless script may throw as freely as any other.
    /// </summary>
    private static JgsValue Pause(
        IReadOnlyList<JgsValue> args, CancellationToken cancellationToken, int line, int col)
    {
        ArityRange("pause", args, 0, 1, line, col);

        if (args.Count == 0)
        {
            if (_pausesEnabled)
            {
                _ = WaitForPress(
                    "pause",
                    "waiting for a key needs a window — say pause(seconds) for a wait a batch run "
                    + "can finish",
                    cancellationToken,
                    line,
                    col);
            }

            return JgsValue.Null;
        }

        if (args[0].Type == JgsType.String)
        {
            string word = args[0].AsString;
            string was = _pausesEnabled ? "on" : "off";
            switch (word.ToLowerInvariant())
            {
                case "on":
                    _pausesEnabled = true;
                    break;
                case "off":
                    _pausesEnabled = false;
                    break;
                case "query":
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"pause: '{word}' is not a word here; it is 'on', 'off' or 'query'.");
            }

            // Every one of the three answers the state as it was before the call, which is what makes
            // pause('off') … pause(old) put back whatever was there rather than guessing 'on'.
            return JgsValue.Str(was);
        }

        double seconds = Num("pause", args, 0, line, col);
        if (_pausesEnabled && seconds > 0 && !double.IsNaN(seconds))
        {
            PumpWait(TimeSpan.FromSeconds(System.Math.Min(seconds, 3600)), cancellationToken);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>[x, y] = ginput(n)</c>, <c>[x, y] = ginput</c> and <c>[x, y, button] = ginput(___)</c>:
    /// points read off the figure by clicking them.
    /// </summary>
    /// <remarks>
    /// The points come back in the current axes' data coordinates, read through the axes'
    /// <c>CurrentPoint</c> — which the window has been recording since M75 whether or not anything
    /// asked. The bare form collects until a key is pressed, which is MATLAB's Enter; any key ends it
    /// here, and the key itself is not one of the points.
    /// </remarks>
    private static JgsValue[] Ginput(
        IReadOnlyList<JgsValue> args, int wanted, CancellationToken cancellationToken, int line, int col)
    {
        ArityRange("ginput", args, 0, 1, line, col);
        int asked = args.Count == 1 ? Count("ginput", args, 0, line, col) : int.MaxValue;
        if (args.Count == 1 && asked < 0)
        {
            throw new JgsRuntimeException(line, col, "ginput: the number of points is not negative.");
        }

        var xs = new List<double>();
        var ys = new List<double>();
        var buttons = new List<double>();

        while (xs.Count < asked)
        {
            ScriptInput input = WaitForPress(
                "ginput",
                "picking points off a chart needs a window — a batch run has nowhere to click",
                cancellationToken,
                line,
                col);

            if (input.Kind == ScriptInputKind.Key)
            {
                // A key ends the collection. In the counted form it ends it early, which is MATLAB's
                // behaviour too: fewer points come back than were asked for.
                break;
            }

            (double x, double y) = DataPointOf(input);
            xs.Add(x);
            ys.Add(y);
            buttons.Add(input.Button);
        }

        JgsValue[] answers =
        [
            JgsMatrix.FromColumnMajor([.. xs], xs.Count, 1),
            JgsMatrix.FromColumnMajor([.. ys], ys.Count, 1),
            JgsMatrix.FromColumnMajor([.. buttons], buttons.Count, 1),
        ];

        return wanted >= 3 ? answers : [.. answers.Take(System.Math.Max(1, wanted))];
    }

    /// <summary>
    /// Where a click landed, in the current axes' own coordinates. The axes has been recording that
    /// since M75, so this reads it rather than mapping the pixel a second time — a click means one
    /// place, and two readings of it would be two chances to disagree.
    /// </summary>
    private static (double X, double Y) DataPointOf(ScriptInput input)
    {
        if (JGraph.Api.JG.CurrentAxesOrNull is { } axes)
        {
            (Core.Primitives.Vector3D front, _) = axes.CurrentPoint;
            return (front.X, front.Y);
        }

        return (input.X, input.Y);
    }

    /// <summary>
    /// Waits for the next key or mouse button, refusing by name where there is no window to press
    /// anything in.
    /// </summary>
    private static ScriptInput WaitForPress(
        string verb, string instead, CancellationToken cancellationToken, int line, int col)
    {
        if (!ScriptEventQueue.PumpInstalled)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: {instead}.");
        }

        long from = ScriptInputWatch.Count;
        long deadline = Environment.TickCount64 + (long)WaitLimit.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            // A slice at a time, draining callbacks between them: a wait is one of MATLAB's
            // interruption points, so a click during it is answered during it.
            PumpWait(PumpSlice, cancellationToken);

            (ScriptInput input, long now) = ScriptInputWatch.Latest;
            if (now > from)
            {
                return input;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{verb}: nothing was pressed for an hour, so the wait was given up rather than held "
            + "for the rest of the session.");
    }
}
