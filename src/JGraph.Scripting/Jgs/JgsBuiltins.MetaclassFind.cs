namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>findobj(mc, ...)</c> of one metaclass (open item 347, ADR 0384; probe p8d1 o1 to o14 and
/// p8d_mc): R2025b searches the class and, below it, its properties, its methods and its events, in that
/// order, and answers the members it keeps as a column of their class, the class itself as the 1-by-1
/// it is, or a 0-by-0 <c>matlab.metadata.Class</c> when it keeps none; <c>-depth</c> under 2 searches
/// the class alone. A search that keeps members of several kinds is a heterogeneous
/// <c>matlab.metadata.MetaData</c> array there, which is not modelled here and is refused by name;
/// so is R2025b's hidden walk (a value class's <c>transpose</c>, <c>reshape</c>, <c>disp</c> and their
/// kind are members it finds that no list here holds).
/// </summary>
internal static partial class JgsBuiltins
{
    private static JgsValue FindInMetaclass(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue mc = args[0];
        int depth = int.MaxValue;
        for (int i = 1; i + 1 < args.Count; i++)
        {
            if (IsTextScalar(args[i]) && TextOf(args[i]).Equals("-depth", StringComparison.OrdinalIgnoreCase)
                && args[i + 1].Type is JgsType.Number or JgsType.Bool)
            {
                depth = (int)System.Math.Max(0, System.Math.Min(int.MaxValue, args[i + 1].AsNumber));
            }
        }

        var kept = new List<(Dictionary<string, JgsValue> Element, string ClassName, string[] Fields)>();
        Dictionary<string, JgsValue> self = mc.AsStruct;
        if (MetadataSearchKeeps(self, args, line, col))
        {
            kept.Add((self, mc.ClassName!, [.. self.Keys]));
        }

        if (depth >= 2)
        {
            foreach (string list in (ReadOnlySpan<string>)["PropertyList", "MethodList", "EventList"])
            {
                // A metaclass rendered past its depth makes its lists when they are read.
                JgsValue? members = self.TryGetValue(list, out JgsValue? held) ? held : MetadataMember(mc, list, null, line, col);
                if (members is null || members.Type != JgsType.Struct || members.ClassName is null)
                {
                    continue;
                }

                JgsStructArray payload = members.AsStructArray;
                foreach (Dictionary<string, JgsValue> element in payload.Elements)
                {
                    if (MetadataSearchKeeps(element, args, line, col))
                    {
                        kept.Add((element, members.ClassName, payload.FieldNames));
                    }
                }
            }
        }

        if (kept.Count == 0)
        {
            return EmptyMetaClass();
        }

        if (kept.Exists(k => k.ClassName != kept[0].ClassName))
        {
            throw new JgsRuntimeException(line, col, "JGraph:findobj:HeterogeneousMetadata",
                "findobj kept members of several kinds of this metaclass; a heterogeneous matlab.metadata.MetaData array is not modelled here.");
        }

        if (ReferenceEquals(kept[0].Element, self))
        {
            return mc;
        }

        var copies = new List<Dictionary<string, JgsValue>>(kept.Count);
        foreach ((Dictionary<string, JgsValue> element, _, _) in kept)
        {
            var copy = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, JgsValue held) in element)
            {
                copy[name] = JgsValue.Share(held);
            }

            copies.Add(copy);
        }

        return MetaColumn(copies, kept[0].Fields, kept[0].ClassName);
    }
}
