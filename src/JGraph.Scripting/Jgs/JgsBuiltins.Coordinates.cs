using System;
using System.Collections.Generic;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The four coordinate conversions of <c>specfun</c> (M106): <c>cart2pol</c>, <c>pol2cart</c>,
/// <c>cart2sph</c> and <c>sph2cart</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each one is written as its own definition and nothing else — <c>cart2pol</c> is an
/// <c>atan2</c> and a <c>hypot</c>, <c>sph2cart</c> is three products of a sine and a cosine — over
/// the same pairwise engine every other two-argument numeric builtin uses. That is only possible
/// because M106 taught that engine to expand implicitly and to keep the shape it was handed; before
/// it did, <c>cart2pol</c> of a column and a row would have been refused and <c>cart2pol</c> of a
/// matrix would have come back as a row.
/// </para>
/// <para>
/// The third slot is a passenger, not a coordinate: the height a polar or cylindrical call carries
/// is handed straight back, so it keeps its own size even when the two coordinates beside it were
/// expanded into something larger. That is MATLAB's answer, and it is the one thing about these
/// four that reading the formula does not tell you.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the coordinate conversions into <paramref name="env"/>.</summary>
    internal static void RegisterCoordinateBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
            {
                MultiOutput = both,
            }));

        DefineBoth("cart2pol", CartesianToPolar);
        DefineBoth("pol2cart", PolarToCartesian);
        DefineBoth("cart2sph", CartesianToSpherical);
        DefineBoth("sph2cart", SphericalToCartesian);
    }

    /// <summary>
    /// <c>[theta, rho] = cart2pol(x, y)</c> and <c>[theta, rho, z] = cart2pol(x, y, z)</c>: the
    /// angle in the correct quadrant and the distance from the origin.
    /// </summary>
    private static JgsValue[] CartesianToPolar(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        CoordinateArity("cart2pol", args, 2, 3, wanted, "z", line, col);
        RealFloat("cart2pol", "atan2", args[0], line, col);
        RealFloat("cart2pol", "atan2", args[1], line, col);

        JgsValue theta = Zip("cart2pol", args[1], args[0], Math.Atan2, line, col);
        JgsValue rho = Zip("cart2pol", args[0], args[1], double.Hypot, line, col);
        return wanted >= 3 ? [theta, rho, args[2]] : Outputs(wanted, theta, rho);
    }

    /// <summary>
    /// <c>[x, y] = pol2cart(theta, rho)</c> and <c>[x, y, z] = pol2cart(theta, rho, z)</c>: the
    /// point an angle and a distance name.
    /// </summary>
    private static JgsValue[] PolarToCartesian(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        CoordinateArity("pol2cart", args, 2, 3, wanted, "z", line, col);
        RealFloat("pol2cart", "cos", args[0], line, col);
        RealFloat("pol2cart", "cos", args[1], line, col);

        JgsValue x = Zip("pol2cart", args[1], args[0], static (r, t) => r * Math.Cos(t), line, col);
        JgsValue y = Zip("pol2cart", args[1], args[0], static (r, t) => r * Math.Sin(t), line, col);
        return wanted >= 3 ? [x, y, args[2]] : Outputs(wanted, x, y);
    }

    /// <summary>
    /// <c>[azimuth, elevation, r] = cart2sph(x, y, z)</c>: the two angles and the radius of a point
    /// given by its Cartesian coordinates.
    /// </summary>
    private static JgsValue[] CartesianToSpherical(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        CoordinateArity("cart2sph", args, 3, 3, wanted, "r", line, col);
        for (int i = 0; i < 3; i++)
        {
            RealFloat("cart2sph", "hypot", args[i], line, col);
        }

        // The planar distance is measured once and then used twice, which is what keeps the radius
        // and the elevation consistent with each other at the poles.
        JgsValue flat = Zip("cart2sph", args[0], args[1], double.Hypot, line, col);
        JgsValue radius = Zip("cart2sph", flat, args[2], double.Hypot, line, col);
        JgsValue elevation = Zip("cart2sph", args[2], flat, Math.Atan2, line, col);
        JgsValue azimuth = Zip("cart2sph", args[1], args[0], Math.Atan2, line, col);
        return Outputs(wanted, azimuth, elevation, radius);
    }

    /// <summary>
    /// <c>[x, y, z] = sph2cart(azimuth, elevation, r)</c>: the point two angles and a radius name.
    /// </summary>
    private static JgsValue[] SphericalToCartesian(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        CoordinateArity("sph2cart", args, 3, 3, wanted, "z", line, col);
        for (int i = 0; i < 3; i++)
        {
            RealFloat("sph2cart", "cos", args[i], line, col);
        }

        JgsValue z = Zip("sph2cart", args[2], args[1], static (r, e) => r * Math.Sin(e), line, col);
        JgsValue flat = Zip("sph2cart", args[2], args[1], static (r, e) => r * Math.Cos(e), line, col);
        JgsValue x = Zip("sph2cart", flat, args[0], static (r, a) => r * Math.Cos(a), line, col);
        JgsValue y = Zip("sph2cart", flat, args[0], static (r, a) => r * Math.Sin(a), line, col);
        return Outputs(wanted, x, y, z);
    }

    /// <summary>
    /// The two arity questions these four share: too few arguments is MATLAB's own
    /// <c>minrhs</c>, and asking for the passenger output without handing one over is the
    /// unassigned-output complaint the interpreted original raises when it reaches the end of the
    /// file with that variable never written.
    /// </summary>
    private static void CoordinateArity(
        string name, IReadOnlyList<JgsValue> args, int least, int most, int wanted, string passenger,
        int line, int col)
    {
        if (args.Count < least)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        if (wanted > args.Count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unassignedOutputs",
                $"Output argument \"{passenger}\" (and possibly others) not assigned a value in the "
                + $"execution with \"{name}\" function.");
        }
    }

    /// <summary>
    /// Refuses what the elementary function underneath would refuse: an integer class or a mask has
    /// no trigonometry in MATLAB at all, and the two conversions that take an arctangent refuse a
    /// complex argument as well.
    /// </summary>
    private static void RealFloat(string name, string underneath, JgsValue value, int line, int col)
    {
        if (IsLogicalValue(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function '{underneath}' for input arguments of type 'logical'.");
        }

        if (value.NumericClass is not (JgsNumericClass.Double or JgsNumericClass.Single))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function '{underneath}' for input arguments of type "
                + $"'{value.NumericClass.MatlabName()}'.");
        }

        if (!HasComplexPart(value))
        {
            return;
        }

        // The two conversions that measure an angle from the coordinates reach an arctangent, which
        // has no complex reading at all; the two that build coordinates from an angle reach a
        // cosine, which does — and that one is not implemented here.
        if (name is "cart2pol" or "cart2sph")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:atan2:complexArgument", "Inputs must be real.");
        }

        throw new JgsRuntimeException(line, col,
            $"{name} of a complex angle is not supported here.");
    }
}
