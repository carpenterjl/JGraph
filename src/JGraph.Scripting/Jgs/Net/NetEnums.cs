namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// .NET enumerations in MATLAB's verbs (interop plan, stage 4, ADR 0177), as R2025b answers them
/// (net_enums, probe4):
/// <list type="bullet">
/// <item><c>enumeration</c> of a type (by name or by a member) prints its members in value order, and
/// answers them only when there is one, since a .NET object is a scalar and a list of two is a
/// concatenation R2025b refuses.</item>
/// <item>A relational operator between two enum members compares their values — across two enum
/// types too; between a member and anything else it is false (<c>c == 1</c>, <c>c == 'Green'</c>).</item>
/// <item><c>bitand</c>, <c>bitor</c> and <c>bitxor</c> combine two members of one <c>[Flags]</c> type
/// into a member of it; a type without <c>[Flags]</c>, or a first operand that is no member, is not
/// numeric to them, and a second operand of another kind has no method.</item>
/// </list>
/// </summary>
internal static class NetEnums
{
    /// <summary>A type's members, in the order .NET sorts them (by value).</summary>
    public static object[] Members(Type type) => Enum.GetValues(type).Cast<object>().ToArray();

    /// <summary>
    /// What <c>enumeration</c> prints for <paramref name="type"/>, blank lines included: a blank line, the
    /// heading, a blank line, the members indented four, a blank line (probe4c's raw capture).
    /// </summary>
    public static string Listing(Type type)
    {
        string className = NetNames.ClassName(type);
        if (!type.IsEnum)
        {
            return $"No enumeration members for class {className}.\n";
        }

        var text = new System.Text.StringBuilder($"\nEnumeration members for class '{className}':\n\n");
        foreach (string name in Enum.GetNames(type))
        {
            text.Append("    ").Append(name).Append('\n');
        }

        return text.Append('\n').ToString();
    }

    /// <summary>What <c>enumeration</c> prints for a name that is no class.</summary>
    public static string NoClass(string name) => $"No class '{name}'.\n";

    /// <summary>
    /// <c>m = enumeration(type)</c>: the one member, or R2025b's refusal to concatenate two; the names
    /// as a cell column for a second output.
    /// </summary>
    public static JgsValue[] Outputs(Type type, int wanted, int line, int col)
    {
        object[] members = type.IsEnum ? Members(type) : [];
        string className = NetNames.ClassName(type);
        if (members.Length > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:concatenationScalar",
                $"Concatenating '{className}' objects is not supported because '{className}' objects can only be scalar.");
        }

        JgsValue first = members.Length == 1 ? JgsValue.External(new NetObject(members[0], type)) : JgsEmpty.Zero();
        if (wanted < 2)
        {
            return [first];
        }

        JgsValue names = JgsValue.Cell(members.Select(m => JgsValue.Str(m.ToString()!)).ToArray());
        names.Reshape(members.Length, 1);
        return [first, names];
    }

    /// <summary>
    /// A relational operator with an enum member on either side, or null when neither is one: two
    /// members compare by value, and a member against anything else is unequal and unordered.
    /// </summary>
    public static JgsValue? Compare(TokenType op, JgsValue left, JgsValue right)
    {
        object? a = MemberOf(left);
        object? b = MemberOf(right);
        if (a is null && b is null)
        {
            return null;
        }

        if (op is not (TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.LessEqual
            or TokenType.Greater or TokenType.GreaterEqual))
        {
            return null;
        }

        if (a is null || b is null)
        {
            return JgsValue.Bool(op == TokenType.BangEqual);
        }

        int order = Value(a).CompareTo(Value(b));
        return JgsValue.Bool(op switch
        {
            TokenType.EqualEqual => order == 0,
            TokenType.BangEqual => order != 0,
            TokenType.Less => order < 0,
            TokenType.LessEqual => order <= 0,
            TokenType.Greater => order > 0,
            _ => order >= 0,
        });
    }

    /// <summary>
    /// An enum member's name, which is what the text verbs compare it by: <c>strcmp(c, 'Green')</c>,
    /// <c>isequal(c, 'Green')</c> and <c>ismember(c, {'Red', 'Green'})</c> are true (probe4) while
    /// <c>c == 'Green'</c> is not. Null for anything else.
    /// </summary>
    public static string? NameOf(JgsValue value) => MemberOf(value)?.ToString();

    /// <summary>
    /// <c>ismember(x, set)</c> with a .NET value on either side: an enum member is in a set holding an
    /// equal member or its name; any other .NET value is in a set holding a value <c>isequal</c> to it.
    /// </summary>
    public static bool IsMemberOf(JgsValue subject, JgsValue set)
    {
        IEnumerable<JgsValue> candidates = set.Type == JgsType.Cell ? set.AsCell
            : set.IsStringArray ? set.BoxedElements().Select(static e => JgsValue.StringScalar(e.AsString))
            : [set];
        return candidates.Any(candidate => NetEquality.DeepEquals(subject, candidate, nanEqual: false));
    }

    /// <summary>Whether a value is a .NET enum member.</summary>
    public static bool IsMember(JgsValue value) => MemberOf(value) is not null;

    /// <summary>
    /// <c>bitand</c>, <c>bitor</c> or <c>bitxor</c> (<paramref name="name"/>) with a .NET value among
    /// its operands. An operand that is not numeric to it is refused under the builtin's own
    /// identifier (R2025b: <c>MATLAB:Bitor:operandsNotNumeric</c>).
    /// </summary>
    public static JgsValue Bitwise(
        string name, JgsValue left, JgsValue right, bool withType, Func<ulong, ulong, ulong> op, int line, int col)
    {
        object? a = MemberOf(left);
        if (a is null || !a.GetType().IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            // Each builtin's own spelling (probe4c): MATLAB:Bitor:, MATLAB:Bitand:, MATLAB:bitxor:.
            string owner = name == "bitxor" ? "bitxor" : char.ToUpperInvariant(name[0]) + name[1..];
            throw new JgsRuntimeException(line, col, $"MATLAB:{owner}:operandsNotNumeric", "Operands to BIT Ops must be numeric.");
        }

        Type type = a.GetType();
        if (withType || MemberOf(right) is not { } b || b.GetType() != type)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"No method '{name}' with matching signature found for class '{NetNames.ClassName(type)}'.");
        }

        ulong bits = op(Bits(a), Bits(b));
        object combined = IsSigned(type) ? Enum.ToObject(type, unchecked((long)bits)) : Enum.ToObject(type, bits);
        return JgsValue.External(new NetObject(combined, type));
    }

    private static object? MemberOf(JgsValue value) =>
        value.AsExternalOrNull() is NetObject { Target: { } held } net && net.Type.IsEnum ? held : null;

    private static bool IsSigned(Type enumType) =>
        Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

    private static ulong Bits(object member) =>
        IsSigned(member.GetType())
            ? unchecked((ulong)Convert.ToInt64(member, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A member's value for ordering, exact across signed and unsigned underlying types.</summary>
    private static decimal Value(object member) =>
        IsSigned(member.GetType())
            ? Convert.ToInt64(member, System.Globalization.CultureInfo.InvariantCulture)
            : Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture);
}
