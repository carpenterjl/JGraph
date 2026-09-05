using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reading a number out of text the way MATLAB's <c>str2double</c> does — which is also what
/// <c>double("5")</c> and its class-constructor cousins do to a string array.
/// </summary>
/// <remarks>
/// <para>
/// <c>str2double</c> is not <c>sscanf</c> and not <c>eval</c>: the whole text has to be one number,
/// spelled the way MATLAB writes one. That is a sign, digits with an optional point, an exponent
/// introduced by <c>e</c> or <c>d</c>, the words <c>Inf</c> and <c>NaN</c> in any case, thousands
/// separated by commas, and a complex number as a real part, a signed imaginary part, or both, with
/// <c>i</c> or <c>j</c> closing the imaginary part. Anything else — <c>'1 2'</c>, <c>'0x10'</c>,
/// <c>'pi'</c>, <c>'5;'</c> — is NaN, which is the one answer a caller can test for.
/// </para>
/// <para>
/// The .NET parser this replaced accepted <c>Infinity</c> and <c>∞</c> and refused <c>Inf</c>, read
/// no complex form and no comma, and was fed through <c>Str</c>, so a number or an empty array
/// raised an error where MATLAB answers NaN.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>One real number in MATLAB's spelling: digits, an optional exponent, or Inf/NaN.</summary>
    private const string RealNumberPattern = @"(?:\d+\.?\d*|\.\d+)(?:[eEdD][+-]?\d+)?|[iI][nN][fF]|[nN][aA][nN]";

    /// <summary>
    /// The whole of what <c>str2double</c> reads. The first alternative is a real part with an
    /// optional signed imaginary part after it; the second is an imaginary part on its own. The
    /// imaginary magnitude is optional in both, because <c>'i'</c> and <c>'3-i'</c> are numbers.
    /// </summary>
    private static readonly Regex NumberTextPattern = new(
        "^(?:(?<re>[+-]?(?:" + RealNumberPattern + @"))(?:\s*(?<is>[+-])\s*(?<im>" + RealNumberPattern + ")?(?<unit>[ijIJ]))?"
        + @"|(?<is>[+-])?\s*(?<im>" + RealNumberPattern + ")?(?<unit>[ijIJ]))$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>Thousands separators: a comma with a digit on either side.</summary>
    private static readonly Regex ThousandsComma = new(@"(?<=\d),(?=\d)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The number a piece of text spells, or NaN when it spells none. A complex spelling answers a
    /// complex value.
    /// </summary>
    internal static JgsValue NumberSpelledBy(string text)
    {
        string trimmed = ThousandsComma.Replace(text.Trim(), string.Empty);
        Match match = NumberTextPattern.Match(trimmed);
        if (!match.Success)
        {
            return JgsValue.Number(double.NaN);
        }

        double real = match.Groups["re"].Success ? RealNumberOf(match.Groups["re"].Value) : 0;
        if (!match.Groups["unit"].Success)
        {
            return JgsValue.Number(real);
        }

        double imaginary = match.Groups["im"].Success ? RealNumberOf(match.Groups["im"].Value) : 1;
        if (match.Groups["is"].Success && match.Groups["is"].Value == "-")
        {
            imaginary = -imaginary;
        }

        return JgsValue.ComplexNum(new Complex(real, imaginary));
    }

    /// <summary>One real number matched by <see cref="RealNumberPattern"/>, with an optional sign in front.</summary>
    private static double RealNumberOf(string spelling)
    {
        bool negative = spelling.StartsWith('-');
        string bare = spelling.TrimStart('+', '-');
        double magnitude;
        if (bare.Equals("inf", StringComparison.OrdinalIgnoreCase))
        {
            magnitude = double.PositiveInfinity;
        }
        else if (bare.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            magnitude = double.NaN;
        }
        else
        {
            // Fortran's 'd' exponent is one MATLAB still reads: 1d3 is 1000.
            magnitude = double.Parse(bare.Replace('d', 'e').Replace('D', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// <c>str2double</c> over any value: a char row answers one number, a string array or a cell
    /// answers one per element in the same shape (NaN for an element that is not text), an empty
    /// cell answers the 0-by-0 empty, and anything that is not text at all answers NaN.
    /// </summary>
    private static JgsValue NumbersSpelledBy(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            return NumberSpelledBy(value.AsString);
        }

        if (!value.IsStringArray && value.Type != JgsType.Cell)
        {
            return JgsValue.Number(double.NaN);
        }

        JgsValue[] pieces = value.IsStringArray ? value.BoxedElements() : value.AsCell;
        if (pieces.Length == 0)
        {
            return JgsEmpty.Zero();
        }

        var numbers = new JgsValue[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            numbers[i] = pieces[i].Type == JgsType.String && pieces[i].AsString != MissingSentinel
                ? NumberSpelledBy(pieces[i].AsString)
                : JgsValue.Number(double.NaN);
        }

        if (numbers.Length == 1)
        {
            return numbers[0];
        }

        JgsValue answer = JgsValue.Array(numbers);
        answer.TakeShapeOf(value);
        return answer;
    }
}
