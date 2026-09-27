using System.Reflection;
using System.Text;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// How a .NET object shows itself in <c>disp</c> and the echo (interop plan, stage 1, ADR 0174): in
/// JGraph's display layout, the one a <c>classdef</c> object uses, with R2025b's choice of what to
/// list. A <c>System.String</c> shows its text; an enum member its name; anything else its short
/// class name and its public properties and fields — the instance properties, the static ones, the
/// instance fields, then the static ones, which is R2025b's order (net_display). A value that stays
/// .NET shows as <c>[1x1 Class]</c> rather than unfolding.
/// </summary>
internal static class NetDisplay
{
    private const BindingFlags Public = BindingFlags.Public | BindingFlags.FlattenHierarchy;

    public static string Of(NetObject net)
    {
        // R2025b's words for a deleted handle (probe5h), under any class.
        if (net.Deleted)
        {
            return "handle to deleted " + NetNames.ShortName(net.Type);
        }

        if (net.Target is string text)
        {
            return text;
        }

        if (net.Target is not null && net.Type.IsEnum)
        {
            return $"{NetNames.ShortName(net.Type)} enumeration: {net.Target}";
        }

        var sb = new StringBuilder(NetNames.ShortName(net.Type)).Append(" with properties:");
        if (net.NullableOf is { } under)
        {
            sb.Append("\n    HasValue: ").Append(net.Target is null ? "false" : "true");
            if (net.Target is not null)
            {
                sb.Append("\n    Value: ").Append(Shown(NetConvert.ToMatlab(net.Target, under)));
            }

            return sb.ToString();
        }

        foreach ((string name, Func<JgsValue> read) in Members(net))
        {
            JgsValue value;
            try
            {
                value = read();
            }
            catch (JgsException)
            {
                continue; // a getter that throws is left out of the listing
            }

            sb.Append("\n    ").Append(name).Append(": ").Append(Shown(value));
        }

        return sb.ToString();
    }

    /// <summary>
    /// The names <c>properties</c> and <c>fieldnames</c> list and the display shows, each with its
    /// reader: readable non-indexer properties then fields, instance before static.
    /// </summary>
    public static IEnumerable<(string Name, Func<JgsValue> Read)> Members(NetObject net)
    {
        Type type = net.Type;
        object? target = net.Target;
        foreach (BindingFlags scope in new[] { BindingFlags.Instance, BindingFlags.Static | BindingFlags.DeclaredOnly })
        {
            foreach (PropertyInfo property in type.GetProperties(Public | scope))
            {
                if (property.GetIndexParameters().Length == 0 && property.GetGetMethod() is not null)
                {
                    PropertyInfo held = property;
                    yield return (property.Name, () => NetInvoke.Member(net, held.Name, autoCall: true, 0, 0));
                }
            }
        }

        foreach (BindingFlags scope in new[] { BindingFlags.Instance, BindingFlags.Static | BindingFlags.DeclaredOnly })
        {
            foreach (FieldInfo field in type.GetFields(Public | scope))
            {
                FieldInfo held = field;
                yield return (field.Name, () => NetConvert.ToMatlab(
                    held.IsLiteral ? held.GetRawConstantValue() : held.GetValue(held.IsStatic ? null : target), held.FieldType));
            }
        }
    }

    /// <summary>The names of <see cref="Members"/>, without reading any.</summary>
    public static IEnumerable<string> MemberNames(NetObject net) =>
        net.NullableOf is not null ? ["HasValue", "Value"] : Members(net).Select(static m => m.Name);

    internal static string Shown(JgsValue value)
    {
        if (value.Type == JgsType.External)
        {
            return $"[1x1 {value.AsExternal.ClassName}]";
        }

        string line = value.Display().ReplaceLineEndings(" ");
        return line.Length <= 60 ? line : string.Concat(line.AsSpan(0, 57), "...");
    }
}
