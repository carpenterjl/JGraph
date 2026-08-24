using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The complex definitions of the elementwise maths family, and the one registration helper that
/// puts them behind their real spellings (M81).
/// </summary>
/// <remarks>
/// <para>
/// M42 gave <c>exp</c>, <c>log</c> and <c>sqrt</c> a real fast path that bails into a complex answer,
/// through <see cref="MapComplexProducing"/>. Nothing else in the family ever followed, so
/// <c>log2(-1)</c>, <c>asin(2)</c> and <c>acosh(0)</c> answered <c>NaN</c> where MATLAB answers a
/// complex number, and <c>sin(1+2i)</c> was refused outright. This file is the rest of that family.
/// </para>
/// <para>
/// Two shapes of gap, and the same seam closes both. A function whose real domain is a proper subset
/// of the reals gets a <c>staysReal</c> predicate and promotes when an argument leaves it; a function
/// that never leaves the reals for a real argument passes <see cref="Always"/> and gains a complex
/// definition only so that a complex <em>argument</em> has somewhere to go. Neither changes what a
/// real argument inside the domain computes: the flat packed path is still taken whenever every
/// element stays real, which is what keeps a million-element <c>sin</c> as fast as it was.
/// </para>
/// <para>
/// The definitions are written out from the classic <see cref="Complex"/> statics rather than taken
/// from the generic-math interfaces .NET 7 added, because those arrive as explicit interface
/// implementations and because the branch cut is the interesting part of each one: writing
/// <c>acosh</c> as <c>log(z + sqrt(z-1)·sqrt(z+1))</c> rather than <c>log(z + sqrt(z²-1))</c> is what
/// puts the cut on <c>(-inf, 1)</c> where it belongs.
/// </para>
/// <para>
/// Where a value sat exactly on the edge of a domain and answered <c>NaN</c> before, it still does:
/// the predicates admit those points to the real path deliberately, so this milestone adds answers
/// where there were none and changes none that a script could already read.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    // --- Registration ----------------------------------------------------------------------------

    /// <summary>
    /// Registers a complex-producing elementwise builtin: <paramref name="fastReal"/> runs on the flat
    /// real path for as long as <paramref name="staysReal"/> holds of every element, and the whole
    /// array promotes through <paramref name="complexResult"/> the moment one element does not.
    /// </summary>
    /// <remarks>
    /// A static helper taking <paramref name="define"/> rather than a local function, because
    /// <c>Math1</c> is declared separately in four files and a fifth copy of this one is how a family
    /// drifts apart. The registration sites pass their own <c>Define</c> by method group.
    /// </remarks>
    private static void MathX(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define,
        string name,
        Func<double, double> fastReal,
        Func<double, bool> staysReal,
        Func<Complex, Complex> complexResult) =>
        define(name, (args, line, col) =>
        {
            Arity(name, args, 1, line, col);
            return MapComplexProducing(name, args[0], fastReal, staysReal,
                z => JgsValue.ComplexNum(complexResult(z)), line, col);
        });

    // --- Domain predicates -----------------------------------------------------------------------
    //
    // Named rather than inlined, because each one is the claim "here is where this function leaves the
    // reals" and a reader should be able to check it against a table of principal values. NaN belongs
    // to every domain: NaN in, NaN out, without a detour through complex arithmetic.

    /// <summary>A function that never leaves the reals for a real argument.</summary>
    private static bool Always(double x) => true;

    /// <summary>The real domain of <c>log</c>, <c>log2</c> and <c>log10</c>.</summary>
    private static bool NonNegative(double x) => x >= 0 || double.IsNaN(x);

    /// <summary>The real domain of <c>log1p</c>.</summary>
    private static bool AtLeastMinusOne(double x) => x >= -1 || double.IsNaN(x);

    /// <summary>The real domain of <c>asin</c>, <c>acos</c>, <c>atanh</c> and the degree spellings.</summary>
    private static bool InsideUnit(double x) => (x >= -1 && x <= 1) || double.IsNaN(x);

    /// <summary>The real domain of <c>acosh</c>.</summary>
    private static bool AtLeastOne(double x) => x >= 1 || double.IsNaN(x);

    /// <summary>
    /// The real domain of <c>asec</c>, <c>acsc</c> and <c>acoth</c>. Zero is admitted so that the
    /// reciprocal these are written over stays on the real path and keeps answering <c>NaN</c>, rather
    /// than meeting <see cref="Complex"/>'s division by zero and changing an answer nobody asked to
    /// have changed.
    /// </summary>
    private static bool OutsideUnitOrZero(double x) =>
        x <= -1 || x >= 1 || x == 0 || double.IsNaN(x);

    /// <summary>The real domain of <c>asech</c>: the unit interval, with zero answering infinity.</summary>
    private static bool InsideUnitAndNonNegative(double x) =>
        (x >= 0 && x <= 1) || double.IsNaN(x);

    /// <summary>
    /// Whether <c>a^b</c> is a real number: a negative base leaves the reals unless the exponent is a
    /// whole number, which is why <c>(-8)^2</c> is 64 and <c>(-8)^0.5</c> is <c>2·sqrt(2)·i</c>.
    /// </summary>
    /// <remarks>
    /// The binary counterpart of the predicates above, and the only one any operator supplies. Every
    /// other operator passes null and keeps the path it has always taken, which is what holds this
    /// milestone's reach to the one operator that needed it.
    /// </remarks>
    internal static bool PowerStaysReal(double a, double b) =>
        a >= 0 || double.IsNaN(a) || double.IsNaN(b) || b == System.Math.Floor(b);

    // --- Complex definitions ---------------------------------------------------------------------

    /// <summary>Radians in a degree, for the degree spellings.</summary>
    private const double RadiansPerDegree = System.Math.PI / 180.0;

    /// <summary>Degrees in a radian.</summary>
    private const double DegreesPerRadian = 180.0 / System.Math.PI;

    /// <summary>The reciprocal, written as a division so that zero divides to infinity rather than to
    /// the zero <see cref="Complex.Reciprocal"/> hands back.</summary>
    private static Complex Invert(Complex z) => Complex.One / z;

    /// <summary>
    /// <c>asin(z) = -i·log(iz + sqrt(1 - z²))</c>, written out rather than taken from
    /// <see cref="Complex.Asin"/>.
    /// </summary>
    /// <remarks>
    /// .NET and MATLAB approach the branch cut from opposite sides: <c>Complex.Asin(2)</c> answers
    /// <c>1.5708 + 1.3170i</c> where MATLAB answers <c>1.5708 - 1.3170i</c>. Both are principal values
    /// of the same multivalued function, but a script porting from MATLAB reads the sign, so this is
    /// the standard formula, which lands on MATLAB's side. <see cref="ComplexAcos"/> is then written
    /// over it so the pair cannot disagree with each other.
    /// </remarks>
    private static Complex ComplexAsin(Complex z) =>
        -Complex.ImaginaryOne * Complex.Log(
            (Complex.ImaginaryOne * z) + Complex.Sqrt(Complex.One - (z * z)));

    /// <summary><c>acos(z) = π/2 - asin(z)</c>.</summary>
    private static Complex ComplexAcos(Complex z) =>
        (System.Math.PI / 2.0) - ComplexAsin(z);

    /// <summary>
    /// Puts a vanished imaginary part back on the positive side of zero.
    /// </summary>
    /// <remarks>
    /// Complex division writes a negative zero for a real quotient with a negative divisor, and
    /// <c>Atan2(-0, -3)</c> is <c>-pi</c> where <c>Atan2(+0, -3)</c> is <c>+pi</c> — which is the whole
    /// difference between <c>atanh(2)</c> answering MATLAB's <c>0.5493 + 1.5708i</c> and its negation.
    /// A real argument sitting on a branch cut is approached from above, so a zero that arrived by
    /// arithmetic rather than by the caller is a positive one.
    /// </remarks>
    private static Complex FromAbove(Complex z) =>
        z.Imaginary == 0 ? new Complex(z.Real, 0.0) : z;

    /// <summary><c>log2</c> of a complex number, through the natural logarithm.</summary>
    private static Complex ComplexLog2(Complex z) => Complex.Log(z) / System.Math.Log(2.0);

    /// <summary><c>log(1+z)</c>. No small-argument refinement: an argument that small is not what
    /// <c>log1p</c> is reached for once it has gone complex, and a second definition to keep true is
    /// worse than the one this shares with <c>log</c>.</summary>
    private static Complex ComplexLog1P(Complex z) => Complex.Log(Complex.One + z);

    /// <summary><c>exp(z)-1</c>, the companion of <see cref="ComplexLog1P"/>.</summary>
    private static Complex ComplexExpM1(Complex z) => Complex.Exp(z) - Complex.One;

    /// <summary><c>asinh(z) = -i·asin(iz)</c>, which borrows <see cref="ComplexAsin"/>'s branch rather
    /// than choosing a second one from a square root.</summary>
    private static Complex ComplexAsinh(Complex z) =>
        -Complex.ImaginaryOne * ComplexAsin(Complex.ImaginaryOne * z);

    /// <summary><c>acosh(z) = log(z + sqrt(z-1)·sqrt(z+1))</c> — the two-root form, for the cut on
    /// <c>(-inf, 1)</c> rather than the wrong one <c>sqrt(z²-1)</c> gives.</summary>
    private static Complex ComplexAcosh(Complex z) =>
        Complex.Log(z + Complex.Sqrt(z - Complex.One) * Complex.Sqrt(z + Complex.One));

    /// <summary><c>atanh(z) = ½·log((1+z)/(1-z))</c>, with the quotient's zero read from above.</summary>
    private static Complex ComplexAtanh(Complex z) =>
        0.5 * Complex.Log(FromAbove((Complex.One + z) / (Complex.One - z)));

    /// <summary><c>sign(z) = z/|z|</c>, and zero stays zero — MATLAB's definition.</summary>
    private static Complex ComplexSign(Complex z)
    {
        double magnitude = Complex.Abs(z);
        return magnitude == 0 ? Complex.Zero : z / magnitude;
    }

    /// <summary>
    /// Applies a real rounding rule to both parts, which is what MATLAB's <c>floor</c>, <c>ceil</c>,
    /// <c>round</c> and <c>fix</c> do to a complex number.
    /// </summary>
    private static Complex Componentwise(Complex z, Func<double, double> rule) =>
        new(rule(z.Real), rule(z.Imaginary));

    /// <summary>The rounding <c>round</c> uses: halves away from zero, as MATLAB's does.</summary>
    private static double RoundAwayFromZero(double x) =>
        System.Math.Round(x, MidpointRounding.AwayFromZero);
}
