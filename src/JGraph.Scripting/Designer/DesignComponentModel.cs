using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Designer;

/// <summary>
/// A custom UI component's <c>AppTypeData</c> as App Designer's model keeps it beside the code
/// (App Designer window plan W15): what the client's <c>CustomComponentAppTypeData.getSerializableAppTypeData</c>
/// sends and MATLAB stores, measured in R2025b by the probe <c>w15_component</c> (section 2, the
/// control written through <c>MLAPPSerializer</c> and read back through <c>loadApp</c>): the events
/// as a struct array of <c>Name</c> and <c>Description</c>, the <c>update</c> method under
/// <c>Methods</c> with its lines, the managed properties as a struct array of the dialog's fields -
/// the size as a column of texts, the attributes without the public access, the validation functions
/// as a struct array of their names and inputs - and <c>AllowTestCaseAccess</c> only when it is on.
/// A collection with nothing in it is the empty double, as the client's JSON <c>[]</c> decodes.
/// </summary>
internal static class DesignComponentModel
{
    /// <summary>The <c>AppTypeData</c> struct for <paramref name="code"/>; null for an app's code, which has none.</summary>
    public static MatData? AppTypeData(DesignCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!code.IsComponent)
        {
            return null;
        }

        var fields = new List<(string Name, MatData Value)>
        {
            ("HasCallbackEvents", code.Events.Count == 0
                ? Empty()
                : Structs(["Name", "Description"], code.Events.Select(e => new MatData[] { MatData.Text(e.Name), MatData.Text(e.Description) }).ToArray())),
            ("Methods", new MatData.Structs([1, 1], ["update"],
                [[new MatData.Structs([1, 1], ["Name", "Code"], [[MatData.Text("update"), code.UpdateBody.Count == 0 ? Empty() : Row(code.UpdateBody)]])]])),
            ("ManagedUserProperties", code.ManagedProperties.Count == 0 ? Empty() : ManagedProperties(code.ManagedProperties)),
        };
        if (code.AllowTestCaseAccess)
        {
            fields.Add(("AllowTestCaseAccess", new MatData.Numbers("logical", [1, 1], [1])));
        }

        return new MatData.Structs([1, 1], [.. fields.Select(static f => f.Name)], [[.. fields.Select(static f => f.Value)]]);
    }

    private static MatData ManagedProperties(IReadOnlyList<DesignManagedProperty> properties)
    {
        string[] names = ["Name", "Description", "DataType", "DefaultValue", "Size", "Attributes", "ValidationFunctions"];
        MatData[][] elements = properties.Select(p => new MatData[]
        {
            MatData.Text(p.Name),
            MatData.Text(p.Description),
            MatData.Text(p.DataType),
            MatData.Text(p.DefaultValue),
            p.Size.Count == 0 ? Empty() : Column(p.Size),
            p.Attributes.Where(static a => a != DesignManagedProperty.AccessPrefix + "public").ToArray() is { Length: > 0 } kept ? Column(kept) : Empty(),
            ValidationFunctions(p.ValidationFunctions),
        }).ToArray();
        return Structs(names, elements);
    }

    /// <summary>The validation functions: none as the empty double; else a column of structs with <c>Name</c>, and <c>InputArguments</c> when any has inputs (the client leaves the field out of a call without them).</summary>
    private static MatData ValidationFunctions(IReadOnlyList<DesignValidationFunction> functions)
    {
        if (functions.Count == 0)
        {
            return Empty();
        }

        bool inputs = functions.Any(static f => f.InputArguments.Length > 0);
        string[] names = inputs ? ["Name", "InputArguments"] : ["Name"];
        MatData[][] elements = functions
            .Select(f => inputs ? new MatData[] { MatData.Text(f.Name), MatData.Text(f.InputArguments) } : [MatData.Text(f.Name)])
            .ToArray();
        return Structs(names, elements);
    }

    /// <summary>A column of structs, as <c>jsondecode</c> gives an array of like objects.</summary>
    private static MatData.Structs Structs(string[] names, MatData[][] elements) => new([elements.Length, 1], names, elements);

    /// <summary>A column of texts, as <c>jsondecode</c> gives an array of strings.</summary>
    private static MatData.Cells Column(IReadOnlyList<string> texts) => new([texts.Count, 1], texts.Select(static t => (MatData)MatData.Text(t)).ToArray());

    /// <summary>A row of texts, as App Designer's serializer keeps a method's lines.</summary>
    private static MatData.Cells Row(IReadOnlyList<string> texts) => new([1, texts.Count], texts.Select(static t => (MatData)MatData.Text(t)).ToArray());

    /// <summary>The empty double, which is what a JSON <c>[]</c> becomes.</summary>
    private static MatData.Numbers Empty() => new("double", [0, 0], []);
}
