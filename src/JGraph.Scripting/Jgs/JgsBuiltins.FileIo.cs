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
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineMany(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name,
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
        env.Declare("image", JgsValue.Function(new BuiltinFunction(
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
        double[]? x = null, y = null, limits = null;
        double[,]? c = null;

        // The name-value spelling, image('XData', x, 'YData', y, 'CData', C), is a different shape
        // from the positional one rather than a tail on it, so it is read first and separately.
        if (args.Count >= 2 && args.Count % 2 == 0 && args[0].Type == JgsType.String)
        {
            for (int i = 0; i + 1 < args.Count; i += 2)
            {
                string key = Str(verb, args, i, line, col);
                if (key.Equals("CData", StringComparison.OrdinalIgnoreCase))
                {
                    c = Matrix(verb, args, i + 1, line, col);
                }
                else if (key.Equals("XData", StringComparison.OrdinalIgnoreCase))
                {
                    x = DoubleArray(verb, args, i + 1, line, col);
                }
                else if (key.Equals("YData", StringComparison.OrdinalIgnoreCase))
                {
                    y = DoubleArray(verb, args, i + 1, line, col);
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb} takes 'CData', 'XData' and 'YData', but got '{key}'.");
                }
            }

            if (c is null)
            {
                throw new JgsRuntimeException(line, col, $"{verb} needs a 'CData' array to draw.");
            }
        }
        else
        {
            ArityRange(verb, args, 1, scaled ? 4 : 3, line, col);
            if (args.Count >= 3)
            {
                x = DoubleArray(verb, args, 0, line, col);
                y = DoubleArray(verb, args, 1, line, col);
                c = Matrix(verb, args, 2, line, col);
                if (args.Count == 4)
                {
                    limits = ClimsOf(verb, args, 3, line, col);
                }
            }
            else
            {
                c = Matrix(verb, args, 0, line, col);
                if (args.Count == 2)
                {
                    limits = ClimsOf(verb, args, 1, line, col);
                }
            }
        }

        ImagePlot plot = JG.Image(c);
        if (x is { Length: > 0 })
        {
            plot.XExtent = new DataRange(x[0], x[^1]);
        }

        if (y is { Length: > 0 })
        {
            plot.YExtent = new DataRange(y[0], y[^1]);
        }

        if (limits is not null)
        {
            plot.AutoScaleColor = false;
            plot.ColorMin = limits[0];
            plot.ColorMax = limits[1];
        }

        return JgsHandleRegistry.For(plot);
    }

    /// <summary>The <c>[cmin cmax]</c> pair <c>imagesc</c>'s trailing argument names.</summary>
    private static double[] ClimsOf(string verb, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        double[] pair = DoubleArray(verb, args, at, line, col);
        if (pair.Length != 2)
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
