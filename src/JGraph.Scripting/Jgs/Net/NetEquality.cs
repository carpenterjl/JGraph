namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// <c>isequal</c> with a .NET value on either side (interop plan, stage 1, ADR 0174): one object is
/// equal to itself; two of one class are equal when every public property and field reads equal,
/// which is R2025b's answer for two <c>JGTest.Members</c> built alike (net_basics); a .NET value is
/// never equal to a MATLAB one, a <c>System.String</c> and a char row included.
/// </summary>
internal static class NetEquality
{
    private const int MaxDepth = 8;

    private static bool IsText(JgsValue value) =>
        (value.Type == JgsType.String && !value.IsCharMatrix) || (value.IsStringArray && value.ArrayLength == 1);

    public static bool DeepEquals(JgsValue left, JgsValue right, bool nanEqual) => Equal(left, right, nanEqual, 0);

    private static bool Equal(JgsValue left, JgsValue right, bool nanEqual, int depth)
    {
        // An enum member equals its own name (R2025b: isequal(c, 'Green'), probe4; ADR 0177).
        if (NetEnums.NameOf(left) is { } leftName && IsText(right))
        {
            return leftName == JgsBuiltins.TextOf(right);
        }

        if (NetEnums.NameOf(right) is { } rightName && IsText(left))
        {
            return rightName == JgsBuiltins.TextOf(left);
        }

        if (left.AsExternalOrNull() is not { } a || right.AsExternalOrNull() is not { } b)
        {
            return left.Type != JgsType.External && right.Type != JgsType.External
                && JgsStdlib.DeepEquals(left, right, nanEqual);
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is not NetObject x || b is not NetObject y || x.Type != y.Type)
        {
            return false;
        }

        if (ReferenceEquals(x.Target, y.Target) || (x.Target is string || x.Type.IsPrimitive || x.Type.IsEnum
            ? Equals(x.Target, y.Target)
            : false))
        {
            return true;
        }

        if (depth >= MaxDepth || x.Target is null || y.Target is null)
        {
            return Equals(x.Target, y.Target);
        }

        var theirs = NetDisplay.Members(y).ToDictionary(static m => m.Name, static m => m.Read, StringComparer.Ordinal);
        foreach ((string name, Func<JgsValue> read) in NetDisplay.Members(x))
        {
            JgsValue mine;
            JgsValue other;
            try
            {
                mine = read();
                other = theirs[name]();
            }
            catch (JgsException)
            {
                return false;
            }

            if (!Equal(mine, other, nanEqual, depth + 1))
            {
                return false;
            }
        }

        return true;
    }
}
