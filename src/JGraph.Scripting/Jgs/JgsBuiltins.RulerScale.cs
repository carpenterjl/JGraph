using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>xscale</c>, <c>yscale</c> and <c>zscale</c> (open item 313, ADR 0360): R2025b's ruler verbs for
/// a ruler's <c>Scale</c>, which its <c>rulerFunctions</c> reads as it reads <c>xlim</c>. An axes, an
/// array of axes or nothing (<c>gca</c>) first; then nothing to ask, or <c>'linear'</c> or
/// <c>'log'</c> (a start of one, any case, a string or a one-element cell) to set. A setting call
/// answers nothing, and asking it for an answer is too many outputs (probes p313, p313b).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterRulerScaleBuiltins(JgsEnvironment env)
    {
        foreach (char axle in "xyz")
        {
            string verb = axle + "scale";
            char which = axle;
            JgsValue[] Run(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                ScaleVerb(verb, which, args, wanted, line, col);

            env.Builtins.Register(verb, JgsValue.Function(new BuiltinFunction(verb, (args, line, col) =>
            {
                JgsValue[] answers = Run(args, 1, line, col);
                return answers.Length > 0 ? answers[0] : JgsValue.Null;
            })
            {
                MultiOutput = Run,
                KnowsWhenDiscarded = true,
                AutoCallsBare = true,
            }));
        }
    }

    private static JgsValue[] ScaleVerb(string verb, char axle, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (wanted > 1)
        {
            throw TooManyScaleOutputs(line, col);
        }

        // The axes, an array of them, or the current one; anything else that is a graphics object
        // (alive or not) is refused, and a second argument after something that is not one too.
        List<GraphObject> targets;
        IReadOnlyList<JgsValue> rest = args;
        if (args.Count > 0 && ScaleTargets(args[0]) is { } named)
        {
            targets = named;
            rest = [.. args.Skip(1)];
        }
        else if (args.Count > 0 && (IsDeadHandle(args[0]) || (args[0].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[0], out _))))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:rulerFunctions:InvalidObject", "Invalid or deleted graphics object.");
        }
        else if (args.Count == 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:rulerFunctions:InvalidAxes",
                "When 2 input arguments are specified, the first argument must be an axes.");
        }
        else
        {
            targets = [JG.Gca()];
        }

        var rulers = new List<AxisModel>(targets.Count);
        foreach (GraphObject target in targets)
        {
            switch (target)
            {
                case ColorbarModel:
                    throw new JgsRuntimeException(line, col, "MATLAB:rulerFunctions:ColorBar", $"{verb} is not supported for colorbars.");
                case AxesModel { IsPolar: true }:
                    throw new JgsRuntimeException(line, col, "MATLAB:rulerFunctions:PolarAxes", $"{verb} is not supported for polar axes.");
                case AxesModel axes:
                    rulers.Add(axle switch { 'x' => axes.PrimaryXAxis, 'y' => axes.ActiveYAxis, _ => axes.ZAxis });
                    break;
            }
        }

        if (rest.Count == 0)
        {
            static string Word(AxisModel ruler) => ruler.Scale == AxisScaleType.Logarithmic ? "log" : "linear";
            return rulers.Count == 1
                ? [JgsValue.Str(Word(rulers[0]))]
                : [CellColumn(rulers.Select(Word))];
        }

        if (wanted > 0)
        {
            throw TooManyScaleOutputs(line, col);
        }

        AxisScaleType scale = ScaleWord(rest[0], line, col);
        foreach (AxisModel ruler in rulers)
        {
            // A ruler of times or of categories has no log scale (probe p313b: a datetime ruler).
            if (scale == AxisScaleType.Logarithmic && (ruler.Scale == AxisScaleType.DateTime || ruler.RulerCategories is not null))
            {
                throw JgsGraphicsProperties.LogScaleRefused(line, col);
            }
        }

        foreach (AxisModel ruler in rulers)
        {
            ruler.Scale = scale;
        }

        return [];
    }

    private static JgsRuntimeException TooManyScaleOutputs(int line, int col) =>
        new(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");

    /// <summary>The axes and colour bars a first argument names, or null when it names none of them.</summary>
    private static List<GraphObject>? ScaleTargets(JgsValue value)
    {
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        if (count == 0 || value.Type is not (JgsType.Number or JgsType.Array))
        {
            return null;
        }

        var targets = new List<GraphObject>(count);
        for (int i = 0; i < count; i++)
        {
            if (!JgsHandleRegistry.TryGet(value.Type == JgsType.Array ? value.ElementAt(i) : value, out JgsHandleEntry? entry)
                || entry.Target is not (AxesModel or ColorbarModel))
            {
                return null;
            }

            targets.Add(entry.Target);
        }

        return targets;
    }

    /// <summary>
    /// The scale a value names, as R2025b's enumerated <c>Scale</c> reads it: <c>'linear'</c> or
    /// <c>'log'</c> or a start of one, in any case, as a char row, a string or a one-element cell.
    /// </summary>
    private static AxisScaleType ScaleWord(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell && value.AsCell.Length == 1)
        {
            value = value.AsCell[0];
        }

        if (IsTextScalar(value))
        {
            string word = StrOf("Scale", value, line, col);
            if (word.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidValue",
                    "Error setting property 'Scale' of class 'NumericRuler': Value cannot be empty. Use one of these values: 'linear' / 'log'.");
            }

            if ("linear".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return AxisScaleType.Linear;
            }

            if ("log".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return AxisScaleType.Logarithmic;
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:rulerFunctions:InvalidValueScaling",
            "Invalid scale value. To set ruler scaling use values of 'linear' or 'log'.");
    }
}
