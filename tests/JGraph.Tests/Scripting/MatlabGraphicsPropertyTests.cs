using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The guardrail on M54's property surface. The point of building the table by reflection was that a
/// plot object added by a later milestone should join the handle surface by existing rather than by
/// someone remembering to extend a switch. That only holds if something checks — so this does, and a
/// new browsable property that no bridge understands fails here rather than going quietly missing.
/// </summary>
public sealed class MatlabGraphicsPropertyTests
{
    /// <summary>
    /// Properties deliberately outside the script's reach, each for a reason. A name may only be added
    /// here with one; the list being short is the whole point.
    /// </summary>
    private static readonly Dictionary<string, string> Excused = new(StringComparer.Ordinal)
    {
        ["AxesModel.XAxes"] = "a collection of rulers, reached one at a time through ax.XAxis",
        ["AxesModel.YAxes"] = "a collection of rulers, reached one at a time through ax.YAxis",
        ["AxesModel.Plots"] = "reached as Children, which mints handles rather than exposing the collection",
        ["AxesModel.Annotations"] = "reached as Children",
        ["AxesModel.Lights"] = "reached as Children",
        ["AxesModel.Grid"] = "reached as XGrid/YGrid; the grid is not separately addressable",
        ["AxesModel.Legend"] = "aliased to a handle by the Legend property",
        ["AxesModel.Colorbar"] = "reached as Children when shown",
        ["AxesModel.ZAxis"] = "aliased to a handle by the ZAxis property",
        ["FigureModel.Axes"] = "reached as Children",
        ["FigureModel.Annotations"] = "reached as Children",
        ["LegendModel.Entries"] = "reached as the String property",
        ["AxesModel.PrimaryXAxis"] = "aliased to a handle by the XAxis property",
        ["AxesModel.PrimaryYAxis"] = "aliased to a handle by the YAxis property",
        ["AxesModel.ActiveYAxis"] = "the side yyaxis last named, which is what the YAxis property already answers",
        ["XYPlot.Data"] = "reached as XData and YData, which are the numbers rather than the series object",
    };

    /// <summary>Every concrete figure-object type the model and the object library define.</summary>
    private static IEnumerable<Type> FigureObjectTypes() =>
        new[] { typeof(GraphObject).Assembly, typeof(LinePlot).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && type.IsPublic && typeof(GraphObject).IsAssignableFrom(type));

    public static TheoryData<Type> ModelTypes()
    {
        var data = new TheoryData<Type>();
        foreach (Type type in FigureObjectTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModelTypes))]
    public void EveryBrowsablePropertyIsReachableThroughAHandle(Type type)
    {
        IReadOnlyDictionary<string, GraphicsProperty> table = JgsGraphicsProperties.TableFor(type);

        var missing = new List<string>();
        foreach (PropertyInfo info in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (info.GetIndexParameters().Length > 0 || !info.CanRead)
            {
                continue;
            }

            if (info.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false })
            {
                continue;
            }

            string key = $"{info.DeclaringType?.Name}.{info.Name}";
            if (Excused.ContainsKey(key) || Excused.ContainsKey($"{type.Name}.{info.Name}"))
            {
                continue;
            }

            if (!table.ContainsKey(info.Name))
            {
                missing.Add($"{info.Name} ({info.PropertyType.Name})");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{type.Name} has browsable properties no script can reach: {string.Join(", ", missing)}. "
            + "Either teach ValueBridge the type, add a MATLAB alias, or excuse it here with a reason.");
    }

    [Theory]
    [MemberData(nameof(ModelTypes))]
    public void EveryFigureObjectAnswersTheUniversalProperties(Type type)
    {
        IReadOnlyDictionary<string, GraphicsProperty> table = JgsGraphicsProperties.TableFor(type);
        foreach (string universal in new[] { "type", "tag", "userdata", "parent", "children", "visible" })
        {
            Assert.True(table.ContainsKey(universal), $"{type.Name} does not answer to '{universal}'.");
        }
    }

    [Fact]
    public void ATypeNameIsNeverEmptyAndNeverTheClassName()
    {
        foreach (Type type in FigureObjectTypes())
        {
            // The name is decided by the type alone, so an uninitialized instance is enough and
            // sidesteps the constructors that need arguments (a ruler needs its orientation).
            var instance = (GraphObject)RuntimeHelpers.GetUninitializedObject(type);
            string name = JgsGraphicsProperties.TypeNameOf(instance);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.Equal(name.ToLowerInvariant(), name);
        }
    }
}
