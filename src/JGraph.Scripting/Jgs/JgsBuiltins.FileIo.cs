using System.IO;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Low-level file access (M36): <c>fopen</c>/<c>fclose</c> over the host's file-id table,
/// <c>fread</c>/<c>fwrite</c> with MATLAB's precision words, <c>fgetl</c> line reading — plus the
/// <c>image</c> display verb, which rides along in this wave.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The width in bytes of each precision word <c>fread</c> and <c>fwrite</c> take. M76 widened
    /// this from eleven names to the whole documented set except the bit-width ones, which need a
    /// bit-level cursor this reader has not got and are refused by name rather than rounded up.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> PrecisionWidths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["uint8"] = 1,
            ["int8"] = 1,
            ["char"] = 1,
            ["uchar"] = 1,
            ["schar"] = 1,
            ["uint16"] = 2,
            ["int16"] = 2,
            ["short"] = 2,
            ["ushort"] = 2,
            ["uint32"] = 4,
            ["int32"] = 4,
            ["int"] = 4,
            ["uint"] = 4,
            ["long"] = 4,
            ["ulong"] = 4,
            ["single"] = 4,
            ["float"] = 4,
            ["float32"] = 4,
            ["int64"] = 8,
            ["uint64"] = 8,
            ["double"] = 8,
            ["float64"] = 8,
        };

    /// <summary>Registers the file-handle builtins (and <c>image</c>) into <paramref name="env"/>.</summary>
    private static void RegisterFileIoBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineMany(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => body(args, 1, line, col)[0])
            { MultiOutput = body }));

        DefineMany("fopen", (args, wanted, line, col) => Open(host, args, wanted, line, col));

        Define("fclose", (args, line, col) =>
        {
            Arity("fclose", args, 1, line, col);
            if (args[0].Type == JgsType.String
                && args[0].AsString.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                host.CloseAllFiles();
                return JgsValue.Number(0);
            }

            // MATLAB returns 0 on success and -1 on failure rather than erroring.
            return JgsValue.Number(host.CloseFile(Count("fclose", args, 0, line, col)) ? 0 : -1);
        });

        Define("frewind", (args, line, col) =>
        {
            Arity("frewind", args, 1, line, col);
            StreamOf(host, "frewind", args, line, col).Position = 0;
            return JgsValue.Null;
        });

        Define("fwrite", (args, line, col) => Write(host, args, line, col));

        DefineMany("fread", (args, wanted, line, col) => Read(host, args, wanted, line, col));

        DefineMany("fgetl", (args, wanted, line, col) =>
            ReadLine(host, "fgetl", args, wanted, keepTerminator: false, line, col));

        DefineMany("fgets", (args, wanted, line, col) =>
            ReadLine(host, "fgets", args, wanted, keepTerminator: true, line, col));

        // image draws, so its handle does not echo as `ans` — the rule plot has always had.
        env.DeclareFunction("image", JgsValue.Function(new BuiltinFunction(
            "image", OnNamedAxes((args, line, col) =>
            {
                if (args.Count == 1 && args[0].Type == JgsType.Image)
                {
                    // An image value displays exactly as imshow shows it.
                    env.TryGet("imshow", out JgsValue imshow);
                    return imshow.AsCallable.Call(args, line, col);
                }

                return DrawImage("image", args, scaled: false, line, col);
            }))
        { BindsAnsAsStatement = false }));
    }

    /// <summary>
    /// The shared body of <c>image</c> and <c>imagesc</c>: <c>(C)</c>, <c>(x, y, C)</c>, the
    /// <c>'CData'</c>/<c>'XData'</c>/<c>'YData'</c> pairs, and <c>imagesc</c>'s trailing <c>clims</c>.
    /// <para>
    /// Until M70 both verbs took one argument and refused the rest, which is thirteen documented
    /// forms between them. <c>x</c> and <c>y</c> give the two ends of the span the raster covers —
    /// MATLAB reads only the first and last element of each, whatever length they are — so the whole
    /// family lands on <see cref="ImagePlot.XExtent"/> and <see cref="ImagePlot.YExtent"/>, which
    /// have been on the model since M6.
    /// </para>
    /// </summary>
    private static JgsValue DrawImage(
        string verb, IReadOnlyList<JgsValue> args, bool scaled, int line, int col)
    {
        if (args.Count == 0) throw new JgsRuntimeException(line, col, $"{verb} requires image data.");
        bool highLevel = args[0].Type != JgsType.String;
        int cursor = 0;
        JgsValue? data = null, x = null, y = null;
        double[]? limits = null;
        var properties = new List<(string Name, JgsValue Value)>();
        if (highLevel)
        {
            if (args.Count >= 3 && args[1].Type != JgsType.String && args[2].Type != JgsType.String)
            {
                x = args[0]; y = args[1]; data = args[2]; cursor = 3;
            }
            else { data = args[0]; cursor = 1; }
        }
        while (cursor < args.Count)
        {
            if (scaled && cursor == args.Count - 1 && args[cursor].Type != JgsType.String)
            {
                limits = ClimsOf(verb, args, cursor++, line, col);
                break;
            }
            if (cursor + 1 >= args.Count) throw new JgsRuntimeException(line, col, $"{verb} properties require name/value pairs.");
            string name = Str(verb, args, cursor, line, col);
            JgsValue value = args[cursor + 1]; cursor += 2;
            switch (name.ToLowerInvariant())
            {
                case "cdata": data = value; break;
                case "xdata": x = value; break;
                case "ydata": y = value; break;
                default: properties.Add((name, value)); break;
            }
        }
        if (data is null) throw new JgsRuntimeException(line, col, $"{verb} requires CData.");
        bool holding = JG.IsHolding;
        ImagePlot plot = highLevel ? JG.Image(new double[0,0]) : JG.Gca().AddImage(new double[0,0]);
        var entry = JgsHandleRegistry.EntryFor(plot);
        JgsGraphicsProperties.SetImageCData(entry, data, line, col);
        plot.RowZeroAtTop = false;
        plot.AlphaDataMapping = AlphaMapping.None;
        plot.CDataMapping = scaled ? ColorMapping.Scaled : ColorMapping.Direct;
        if (x is not null) JgsGraphicsProperties.Set(entry, "XData", x, line, col);
        if (y is not null) JgsGraphicsProperties.Set(entry, "YData", y, line, col);
        if (highLevel && !holding)
        {
            JG.Gca().ActiveYAxis.Inverted = true;
            JG.Gca().Layer = JGraph.Core.Model.AxesLayer.Top;
            JG.Gca().SetViewAngles(0, 90);
        }
        foreach (var property in properties) JgsGraphicsProperties.Set(entry, property.Name, property.Value, line, col);
        if (scaled && plot.TrueColors is null)
        {
            plot.AutoScaleColor = true;
            var range = plot.ColorRange;
            JG.Gca().ColorLimits = new DataRange(range.Min, range.Max);
        }
        if (limits is not null)
        {
            JG.Gca().ColorLimits = new DataRange(limits[0], limits[1]);
            plot.AutoScaleColor = false; plot.ColorMin = limits[0]; plot.ColorMax = limits[1];
        }
        return JgsHandleRegistry.For(plot);
    }

    /// <summary>The <c>[cmin cmax]</c> pair <c>imagesc</c>'s trailing argument names.</summary>
    private static double[] ClimsOf(string verb, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        double[] pair = DoubleArray(verb, args, at, line, col);
        if (pair.Length != 2 || !double.IsFinite(pair[0]) || !double.IsFinite(pair[1]) || pair[0] >= pair[1])
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: clims is [cmin cmax], but got {pair.Length} value(s).");
        }

        return pair;
    }

    private static FileStream OpenStream(
        JGraphScriptGlobals host, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int id = Count(name, args, 0, line, col);
        return host.FileFor(id)
            ?? throw new JgsRuntimeException(line, col, $"{name}: file id {id} is not open.");
    }

    private static int WidthOf(string name, string precision, int line, int col) =>
        PrecisionWidths.TryGetValue(precision, out int width)
            ? width
            : throw new JgsRuntimeException(line, col,
                $"{name} does not support the precision '{precision}'. Try uint8, int16, int32, single, or double.");

    private static byte[] EncodeValue(double value, string precision, int width) =>
        precision.ToLowerInvariant() switch
        {
            "uint8" or "char" => [(byte)value],
            "int8" => [unchecked((byte)(sbyte)value)],
            "uint16" => BitConverter.GetBytes((ushort)value),
            "int16" => BitConverter.GetBytes((short)value),
            "uint32" => BitConverter.GetBytes((uint)value),
            "int32" => BitConverter.GetBytes((int)value),
            "single" or "float32" => BitConverter.GetBytes((float)value),
            _ => BitConverter.GetBytes(value),
        };

    private static double DecodeValue(byte[] bytes, string precision) =>
        precision.ToLowerInvariant() switch
        {
            "uint8" or "char" => bytes[0],
            "int8" => (sbyte)bytes[0],
            "uint16" => BitConverter.ToUInt16(bytes),
            "int16" => BitConverter.ToInt16(bytes),
            "uint32" => BitConverter.ToUInt32(bytes),
            "int32" => BitConverter.ToInt32(bytes),
            "single" or "float32" => BitConverter.ToSingle(bytes),
            _ => BitConverter.ToDouble(bytes),
        };
}
