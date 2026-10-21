using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Designer;

/// <content>
/// Component groups in App Designer's tree (open item 150): <c>components.Groups</c>, a 1-by-N cell
/// of structs with <c>Id</c> and <c>ParentGroupId</c> as <c>GroupsModel.getGroupHierarchy</c> builds
/// it, and each member's <c>DesignTimeProperties.GroupId</c>.
/// </content>
public static partial class DesignTreeWriter
{
    private const string GroupsField = "Groups";
    private const string GroupIdField = "GroupId";
    private const string GroupIdName = "Id";
    private const string GroupParentName = "ParentGroupId";

    /// <summary>
    /// The groups the model <paramref name="model"/> (the bytes of an <c>appModel.mat</c>) holds:
    /// each of <c>components.Groups</c> with the code names of the saved components whose
    /// <c>GroupId</c> names it. Empty when the model cannot be read or holds no groups.
    /// </summary>
    public static IReadOnlyList<DesignGroupRecord> ReadGroups(byte[] model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return MatMcosFile.Read(model) is { } file && file.Variable(Variable) is { } components ? ReadGroups(file, components) : [];
    }

    private static List<DesignGroupRecord> ReadGroups(MatMcosFile file, MatData.Kept components)
    {
        var groups = new List<(string Id, string Parent)>();
        foreach ((string name, byte[] element) in MatRaw.StructFields(components.Element) ?? [])
        {
            if (name != GroupsField)
            {
                continue;
            }

            if (MatRaw.CellItems(element) is { } items)
            {
                groups.AddRange(items.Select(GroupOf).OfType<(string, string)>());
            }
            else if (GroupOf(element) is { } one)
            {
                groups.Add(one); // a struct array of one, which MLAPPSerializer accepts too
            }
        }

        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string id, _) in groups)
        {
            members.TryAdd(id, []);
        }

        foreach (McosObject o in file.Objects)
        {
            if (DesignTime(file, o) is { } held
                && held.Fields.Find(static f => f.Name == GroupIdField).Value is MatData.Chars { Characters.Length: > 0 } id
                && members.TryGetValue(id.Characters, out List<string>? list) && !list.Contains(held.CodeName, StringComparer.Ordinal))
            {
                list.Add(held.CodeName);
            }
        }

        return groups.Where(g => members.ContainsKey(g.Id)).DistinctBy(static g => g.Id, StringComparer.Ordinal)
            .Select(g => new DesignGroupRecord(g.Id, g.Parent, members[g.Id])).ToList();

        static (string Id, string Parent)? GroupOf(byte[] element)
        {
            string? id = null;
            string parent = string.Empty;
            foreach ((string name, byte[] field) in MatRaw.StructFields(element) ?? [])
            {
                if (name == GroupIdName)
                {
                    id = MatRaw.Text(field);
                }
                else if (name == GroupParentName)
                {
                    parent = MatRaw.Text(field) ?? string.Empty;
                }
            }

            return id is { Length: > 0 } ? (id, parent) : null;
        }
    }

    /// <summary>
    /// The <c>components</c> variable: the figure under <see cref="FigureField"/> and, when there are
    /// any, the groups as App Designer's serializer saves them
    /// (<c>MLAPPSerializer.getComponentsStructureToSave</c>: a 1-by-N cell of <c>Id</c>/<c>ParentGroupId</c> structs).
    /// </summary>
    internal static MatData.Structs ComponentsVariable(McosObject figure, IReadOnlyList<DesignGroupRecord> groups)
    {
        var fields = new List<(string Name, MatData Value)> { (FigureField, new MatData.Object(figure)) };
        if (groups.Count > 0)
        {
            fields.Add((GroupsField, new MatData.Cells([1, groups.Count],
                groups.Select(static g => (MatData)MatData.Struct((GroupIdName, MatData.Text(g.Id)), (GroupParentName, MatData.Text(g.ParentId)))).ToArray())));
        }

        return MatData.Struct([.. fields]);
    }

    /// <summary>Whether <paramref name="saved"/> names the same groups, by id and parent in the same order, as <paramref name="groups"/>.</summary>
    private static bool SameGroups(IReadOnlyList<DesignGroupRecord> saved, IReadOnlyList<DesignGroupRecord> groups) =>
        saved.Count == groups.Count && saved.Zip(groups).All(static p => p.First.Id == p.Second.Id && p.First.ParentId == p.Second.ParentId);

    /// <summary>Sets the <c>GroupId</c> of <paramref name="o"/>'s design-time record to <paramref name="id"/>; whether it changed.</summary>
    private static bool SetGroupId(MatMcosFile file, McosObject o, string id)
    {
        if (o.Dynamic.Find(d => TextOf(d.Get("DynamicName_")) == "DesignTimeProperties") is not { } designTime
            || designTime.Get("DynamicValue_") is not McosValue.Value { Data: var data })
        {
            return false;
        }

        // A record kept as the file held it is read first; one written here is a struct already.
        MatData.Structs? plain = data as MatData.Structs
            ?? (data is MatData.Kept kept ? MatRaw.Plain(kept.Element, file.Keep) as MatData.Structs : null);
        if (plain is not { Elements: [var values] } record)
        {
            return false;
        }

        int at = Array.IndexOf(record.Fields, GroupIdField);
        if (at < 0)
        {
            // App Designer's record always has it; one written without it gets it after CodeName.
            int after = Array.IndexOf(record.Fields, CodeNameField) + 1;
            string[] fields = [.. record.Fields[..after], GroupIdField, .. record.Fields[after..]];
            MatData[] copy = [.. values[..after], MatData.Text(id), .. values[after..]];
            designTime.Set("DynamicValue_", new McosValue.Value(record with { Fields = fields, Elements = [copy] }));
            return true;
        }

        if (values[at] is MatData.Chars { Characters: var was } && was == id)
        {
            return false;
        }

        MatData[] changed = [.. values];
        changed[at] = MatData.Text(id);
        designTime.Set("DynamicValue_", new McosValue.Value(record with { Elements = [changed] }));
        return true;
    }
}
