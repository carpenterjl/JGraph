using JGraph.Maths.Volumes;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M59: volume visualization. A field in this build is a plain three-dimensional array, so nothing
/// here needs a value type of its own — every verb reads readings on a grid and answers with either
/// numbers or a drawing made of objects that already existed.
/// </summary>
/// <remarks>
/// <para>
/// The whole family shares one shape of argument list: the grid may be given or left out, and leaving
/// it out means the readings are on the whole numbers. <c>isosurface(V, 0.5)</c> and
/// <c>isosurface(X, Y, Z, V, 0.5)</c> are the same call with and without that prefix, and every verb
/// here reads it the same way — see <see cref="ReadScalarField"/> and <see cref="ReadVectorField"/>,
/// which are the only two places that know how.
/// </para>
/// <para>
/// The verbs divide in two. Those that answer with numbers — <c>volumebounds</c>, <c>subvolume</c>,
/// <c>reducevolume</c>, <c>smooth3</c>, <c>curl</c>, <c>divergence</c> — never draw. Those that make
/// a shape answer with the shape's faces and vertices when a script asks for them, and draw it when
/// it does not, which is MATLAB's rule for this family and the reason <c>fv = isosurface(…)</c>
/// followed by <c>patch(fv)</c> works.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterVolumeBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        env.Declare("volumebounds", JgsValue.Function(new BuiltinFunction("volumebounds",
            (args, line, col) => VolumeBounds(args, line, col))));

        env.Declare("subvolume", JgsValue.Function(new BuiltinFunction("subvolume",
            (args, line, col) => Subvolume(args, 1, line, col)[^1])
        {
            MultiOutput = (args, wanted, line, col) => Subvolume(args, wanted, line, col),
        }));

        env.Declare("reducevolume", JgsValue.Function(new BuiltinFunction("reducevolume",
            (args, line, col) => ReduceVolume(args, 1, line, col)[^1])
        {
            MultiOutput = (args, wanted, line, col) => ReduceVolume(args, wanted, line, col),
        }));

        Define("smooth3", (args, line, col) => Smooth3(args, line, col));

        env.Declare("divergence", JgsValue.Function(new BuiltinFunction("divergence",
            (args, line, col) => Divergence(args, line, col))));

        env.Declare("curl", JgsValue.Function(new BuiltinFunction("curl",
            (args, line, col) => Curl(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Curl(args, wanted, line, col),
        }));

        // interp3 reads a plaid grid, which is what JgsBuiltins.Interpolation.Grid.cs does for
        // two, three and n directions alike (M101). It stays registered here, beside the volume
        // names it is used with, because a second Define elsewhere would shadow this one silently.
        Define("interp3", (args, line, col) => SampleGridded("interp3", args, 3, host, line, col));

        RegisterIsoSurfaceBuiltins(env);
        RegisterStreamBuiltins(env);
    }

    // --- Reading a field off the argument list ----------------------------------------------------

    /// <summary>
    /// A scalar field and the arguments left after it. The grid is either the first three arguments
    /// or, when they are missing, the whole numbers along each direction — which is what MATLAB means
    /// by the short form of every verb in this family.
    /// </summary>
    /// <param name="verb">The verb reading the field, for the message when something is wrong.</param>
    /// <param name="args">The whole argument list.</param>
    /// <param name="fields">How many reading arrays follow the grid (one, or three for a vector field).</param>
    /// <param name="line">Source line.</param>
    /// <param name="col">Source column.</param>
    /// <param name="hasGrid">
    /// Whether a grid precedes the readings, when the caller already knows; null leaves it to the
    /// shape test below.
    /// </param>
    private static (ScalarField[] Fields, int Next) ReadFields(
        string verb, IReadOnlyList<JgsValue> args, int fields, int line, int col, bool? hasGrid = null)
    {
        // The grid is present when there are at least three more arrays than the verb needs readings
        // for, and the first of them is the right size to be a grid rather than a reading. A caller
        // that has already worked it out from its own argument count says so instead: streamline's
        // six-argument form is a volume with no grid, which this test cannot tell from a plane with
        // one because both are six arrays of the right shapes.
        bool gridded = hasGrid
            ?? (args.Count >= fields + 3 && LooksLikeGrid(args, fields));
        int at = gridded ? 3 : 0;

        if (args.Count < at + fields)
        {
            throw new JgsRuntimeException(line, col, fields == 1
                ? $"{verb} needs a volume V, or the grid X, Y, Z and then V."
                : $"{verb} needs the three components U, V, W, or the grid X, Y, Z and then U, V, W.");
        }

        var values = new double[fields][,,];
        int[]? shape = null;
        for (int i = 0; i < fields; i++)
        {
            values[i] = ReadVolume(verb, args[at + i], at + i, line, col);
            int[] mine = [values[i].GetLength(0), values[i].GetLength(1), values[i].GetLength(2)];
            shape ??= mine;
            if (!shape.SequenceEqual(mine))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: the volumes have to be the same size, but one is "
                    + $"{string.Join("x", shape)} and another is {string.Join("x", mine)}.");
            }
        }

        int rows = shape![0], columns = shape[1], pages = shape[2];
        double[] x, y, z;
        if (gridded)
        {
            x = GridVector(verb, args[0], 1, columns, line, col);
            y = GridVector(verb, args[1], 0, rows, line, col);
            z = GridVector(verb, args[2], 2, pages, line, col);
        }
        else
        {
            x = Counting(columns);
            y = Counting(rows);
            z = Counting(pages);
        }

        var built = new ScalarField[fields];
        for (int i = 0; i < fields; i++)
        {
            built[i] = new ScalarField(x, y, z, values[i]);
        }

        return (built, at + fields);
    }

    /// <summary>One scalar field and where the arguments after it start.</summary>
    private static (ScalarField Field, int Next) ReadScalarField(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (ScalarField[] fields, int next) = ReadFields(verb, args, 1, line, col);
        return (fields[0], next);
    }

    /// <summary>One vector field and where the arguments after it start.</summary>
    private static (VectorField Field, int Next) ReadVectorField(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool? hasGrid = null)
    {
        (ScalarField[] fields, int next) = ReadFields(verb, args, 3, line, col, hasGrid);
        return (new VectorField(fields[0], fields[1], fields[2]), next);
    }

    /// <summary>
    /// Whether the first three arguments are a grid rather than the readings themselves. A grid is
    /// three arrays that are each either a vector or the same size as the readings that follow, which
    /// is exactly what <c>meshgrid</c> hands back.
    /// </summary>
    private static bool LooksLikeGrid(IReadOnlyList<JgsValue> args, int fields)
    {
        // Three coordinate arrays and then the readings themselves all have to be present and
        // numeric; anything shorter than that is the short form, whatever its first argument looks
        // like.
        if (args.Count < 3 + fields)
        {
            return false;
        }

        for (int i = 0; i < 3 + fields; i++)
        {
            if (args[i].Type is not (JgsType.Array or JgsType.Number or JgsType.Bool))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The positions along one direction, from either a vector or a full grid array as
    /// <c>meshgrid</c> makes one — in which case the positions are read along the one dimension they
    /// vary in.
    /// </summary>
    private static double[] GridVector(
        string verb, JgsValue value, int dimension, int expected, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        double[] flat = ToDoubles(verb, value, line, col);

        if (flat.Length == expected && (dims.Length < 3 || dims.Count(d => d > 1) <= 1))
        {
            return flat;
        }

        if (dims.Length >= 3 || (dims.Length == 2 && dims[0] > 1 && dims[1] > 1))
        {
            // A full grid: step along the one dimension this coordinate varies in.
            int stride = 1;
            for (int d = 0; d < dimension && d < dims.Length; d++)
            {
                stride *= dims[d];
            }

            int length = dimension < dims.Length ? dims[dimension] : 1;
            if (length == expected)
            {
                var positions = new double[expected];
                for (int i = 0; i < expected; i++)
                {
                    positions[i] = flat[i * stride];
                }

                return positions;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{verb}: the grid does not match the volume — it wants {expected} positions along "
            + $"{"xyz"[dimension == 0 ? 1 : dimension == 1 ? 0 : 2]}, but was given {flat.Length}.");
    }

    /// <summary>A volume argument as <c>[row, column, page]</c> readings.</summary>
    private static double[,,] ReadVolume(
        string verb, JgsValue value, int index, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: argument {index + 1} has {dims.Length} dimensions; a volume has three.");
        }

        double[] flat = ToDoubles(verb, value, line, col);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        int pages = dims.Length > 2 ? dims[2] : 1;

        var values = new double[rows, columns, pages];
        for (int p = 0; p < pages; p++)
        {
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    values[r, c, p] = flat[r + (rows * (c + (columns * p)))];
                }
            }
        }

        return values;
    }

    /// <summary>A field's readings back as an N-D array, in the shape they came in.</summary>
    private static JgsValue VolumeValue(ScalarField field)
    {
        int rows = field.Rows, columns = field.Columns, pages = field.Pages;
        var flat = new double[rows * columns * pages];
        for (int p = 0; p < pages; p++)
        {
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    flat[r + (rows * (c + (columns * p)))] = field.Values[r, c, p];
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [rows, columns, pages]);
    }

    /// <summary>The full grid arrays a field sits on, the way <c>meshgrid</c> would have made them.</summary>
    private static (JgsValue X, JgsValue Y, JgsValue Z) GridValues(ScalarField field)
    {
        int rows = field.Rows, columns = field.Columns, pages = field.Pages;
        var gx = new double[rows * columns * pages];
        var gy = new double[gx.Length];
        var gz = new double[gx.Length];

        for (int p = 0; p < pages; p++)
        {
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    int i = r + (rows * (c + (columns * p)));
                    gx[i] = field.X[c];
                    gy[i] = field.Y[r];
                    gz[i] = field.Z[p];
                }
            }
        }

        int[] dims = [rows, columns, pages];
        return (
            JgsMatrix.FromColumnMajorDims(gx, dims),
            JgsMatrix.FromColumnMajorDims(gy, dims),
            JgsMatrix.FromColumnMajorDims(gz, dims));
    }

    // --- The verbs that only answer with numbers --------------------------------------------------

    /// <summary>
    /// <c>volumebounds(X, Y, Z, V)</c>: the box the grid covers, and for a scalar field the range of
    /// its readings as two more numbers.
    /// </summary>
    private static JgsValue VolumeBounds(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("volumebounds", args, 1, 6, line, col);

        if (args.Count >= 6)
        {
            (VectorField field, _) = ReadVectorField("volumebounds", args, line, col);
            return JgsValue.Array(VolumeReduction.Bounds(field.U).Select(JgsValue.Number).ToArray());
        }

        (ScalarField scalar, _) = ReadScalarField("volumebounds", args, line, col);
        double[] box = VolumeReduction.Bounds(scalar);

        // The colour limits are the range of the readings, which is what a script hands to caxis.
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int r = 0; r < scalar.Rows; r++)
        {
            for (int c = 0; c < scalar.Columns; c++)
            {
                for (int p = 0; p < scalar.Pages; p++)
                {
                    double value = scalar.Values[r, c, p];
                    if (double.IsFinite(value))
                    {
                        low = System.Math.Min(low, value);
                        high = System.Math.Max(high, value);
                    }
                }
            }
        }

        double[] answer = double.IsFinite(low) ? [.. box, low, high] : box;
        return JgsValue.Array(answer.Select(JgsValue.Number).ToArray());
    }

    /// <summary>
    /// <c>[NX, NY, NZ, NV] = subvolume(X, Y, Z, V, limits)</c>: the part of a field inside a box.
    /// </summary>
    private static JgsValue[] Subvolume(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("subvolume", args, 2, 5, line, col);
        (ScalarField field, int next) = ReadScalarField("subvolume", args, line, col);
        if (next >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                "subvolume needs a box: [xmin xmax ymin ymax zmin zmax], with NaN for a side to leave alone.");
        }

        double[] box = ToDoubles("subvolume", args[next], line, col);
        if (box.Length != 6)
        {
            throw new JgsRuntimeException(line, col,
                $"subvolume: a box is six numbers, [xmin xmax ymin ymax zmin zmax], but got {box.Length}.");
        }

        ScalarField cut;
        try
        {
            cut = VolumeReduction.Subvolume(field, box);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"subvolume: {ex.Message}");
        }

        return GridAndVolume(cut, wanted);
    }

    /// <summary>
    /// <c>[NX, NY, NZ, NV] = reducevolume(X, Y, Z, V, [Rx Ry Rz])</c>: every n-th reading.
    /// </summary>
    private static JgsValue[] ReduceVolume(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("reducevolume", args, 2, 5, line, col);
        (ScalarField field, int next) = ReadScalarField("reducevolume", args, line, col);
        if (next >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                "reducevolume needs how much to keep: [Rx Ry Rz], or one number for all three.");
        }

        double[] factors = ToDoubles("reducevolume", args[next], line, col);
        if (factors.Length is not (1 or 3) || factors.Any(f => !(f >= 1)))
        {
            throw new JgsRuntimeException(line, col,
                "reducevolume: the reduction is one number or three, each at least 1.");
        }

        int rx = (int)System.Math.Round(factors[0]);
        int ry = factors.Length == 3 ? (int)System.Math.Round(factors[1]) : rx;
        int rz = factors.Length == 3 ? (int)System.Math.Round(factors[2]) : rx;
        return GridAndVolume(VolumeReduction.Reduce(field, rx, ry, rz), wanted);
    }

    /// <summary>
    /// The four answers <c>subvolume</c> and <c>reducevolume</c> share: the new grid and the new
    /// readings, or the readings alone when that is all a script asked for.
    /// </summary>
    private static JgsValue[] GridAndVolume(ScalarField field, int wanted)
    {
        if (wanted <= 1)
        {
            return [VolumeValue(field)];
        }

        (JgsValue x, JgsValue y, JgsValue z) = GridValues(field);
        return wanted switch
        {
            2 => [x, y],
            3 => [x, y, z],
            _ => [x, y, z, VolumeValue(field)],
        };
    }

    /// <summary>
    /// <c>smooth3(V)</c>, <c>smooth3(V, filter)</c>, <c>smooth3(V, filter, size)</c>,
    /// <c>smooth3(V, filter, size, sd)</c>: each reading averaged with the block around it.
    /// </summary>
    private static JgsValue Smooth3(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("smooth3", args, 1, 4, line, col);
        double[,,] readings = ReadVolume("smooth3", args[0], 0, line, col);
        var field = new ScalarField(
            Counting(readings.GetLength(1)),
            Counting(readings.GetLength(0)),
            Counting(readings.GetLength(2)),
            readings);

        bool gaussian = false;
        if (args.Count > 1)
        {
            string filter = Str("smooth3", args, 1, line, col);
            gaussian = filter.Equals("gaussian", StringComparison.OrdinalIgnoreCase);
            if (!gaussian && !filter.Equals("box", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"smooth3: '{filter}' is not a filter here; it is 'box' or 'gaussian'.");
            }
        }

        int[] sizes = [3, 3, 3];
        if (args.Count > 2)
        {
            double[] given = ToDoubles("smooth3", args[2], line, col);
            if (given.Length is not (1 or 3) || given.Any(s => !(s >= 1)))
            {
                throw new JgsRuntimeException(line, col,
                    "smooth3: the block size is one number or three, each at least 1.");
            }

            sizes = given.Length == 1
                ? [(int)given[0], (int)given[0], (int)given[0]]
                : [(int)given[0], (int)given[1], (int)given[2]];
        }

        double deviation = args.Count > 3 ? Num("smooth3", args, 3, line, col) : 0.65;

        // MATLAB writes the block size as [rows columns pages], so the first number is along y.
        return VolumeValue(VolumeReduction.Smooth(
            field, [sizes[1], sizes[0], sizes[2]], gaussian, deviation));
    }

    /// <summary>
    /// <c>divergence(X, Y, Z, U, V, W)</c> and the two-dimensional <c>divergence(X, Y, U, V)</c>:
    /// how much the field spreads out at each point.
    /// </summary>
    private static JgsValue Divergence(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("divergence", args, 2, 6, line, col);
        VectorField field = ReadFlowField("divergence", args, line, col);
        return VolumeValue(field.Divergence());
    }

    /// <summary>
    /// <c>[cx, cy, cz, cav] = curl(X, Y, Z, U, V, W)</c>: how much the field turns, and the angular
    /// velocity that turning amounts to.
    /// </summary>
    private static JgsValue[] Curl(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("curl", args, 2, 6, line, col);
        VectorField field = ReadFlowField("curl", args, line, col);
        (VectorField turning, ScalarField speed) = field.Curl();

        // With one output MATLAB answers the angular velocity for a plane field and the x component
        // of the curl for a field in space, which is the only reading of "the curl" a single number
        // can carry in each case.
        if (wanted <= 1)
        {
            return [field.W.Pages == 1 && IsFlat(field) ? VolumeValue(speed) : VolumeValue(turning.U)];
        }

        return wanted switch
        {
            2 => [VolumeValue(turning.W), VolumeValue(speed)],
            3 => [VolumeValue(turning.U), VolumeValue(turning.V), VolumeValue(turning.W)],
            _ => [
                VolumeValue(turning.U), VolumeValue(turning.V), VolumeValue(turning.W),
                VolumeValue(speed)],
        };
    }

    /// <summary>Whether a vector field has no third component worth speaking of.</summary>
    private static bool IsFlat(VectorField field)
    {
        for (int r = 0; r < field.W.Rows; r++)
        {
            for (int c = 0; c < field.W.Columns; c++)
            {
                for (int p = 0; p < field.W.Pages; p++)
                {
                    if (field.W.Values[r, c, p] != 0)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The field <c>curl</c> and <c>divergence</c> read, which may be a plane rather than a box: a
    /// plane field is the same field one page deep with nothing in its third component, so the same
    /// arithmetic answers both without a second spelling.
    /// </summary>
    private static VectorField ReadFlowField(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool plane = args.Count is 2 or 4;
        if (!plane)
        {
            (VectorField field, _) = ReadVectorField(verb, args, line, col);
            return field;
        }

        int at = args.Count == 4 ? 2 : 0;
        double[,] u = Matrix(verb, args, at, line, col);
        double[,] v = Matrix(verb, args, at + 1, line, col);
        int rows = u.GetLength(0), columns = u.GetLength(1);
        if (v.GetLength(0) != rows || v.GetLength(1) != columns)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the two components have to be the same size.");
        }

        double[] x = at == 2 ? GridVector(verb, args[0], 1, columns, line, col) : Counting(columns);
        double[] y = at == 2 ? GridVector(verb, args[1], 0, rows, line, col) : Counting(rows);
        double[] z = [1];

        var uu = new double[rows, columns, 1];
        var vv = new double[rows, columns, 1];
        var ww = new double[rows, columns, 1];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                uu[r, c, 0] = u[r, c];
                vv[r, c, 0] = v[r, c];
            }
        }

        return new VectorField(
            new ScalarField(x, y, z, uu),
            new ScalarField(x, y, z, vv),
            new ScalarField(x, y, z, ww));
    }
}
