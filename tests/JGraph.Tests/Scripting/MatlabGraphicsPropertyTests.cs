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
        ["AxesModel.BubbleLegend"] = "reached as Children when shown, as the colorbar is",
        ["AxesModel.ZAxis"] = "aliased to a handle by the ZAxis property",
        ["FigureModel.Axes"] = "reached as Children",
        ["FigureModel.Annotations"] = "reached as Children",
        ["FigureModel.ContextMenus"] = "reached as Children",
        ["FigureModel.Components"] = "reached as Children",
        ["UiControlModel.Name"] = "a component answers to MATLAB's names alone, and a uicontrol has no Name (ADR 0198)",
        ["UiControlModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0198)",
        ["UiControlModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it and which a uicontrol answers though it lists none (ADR 0200)",
        ["UiPanelModel.Name"] = "a component answers to MATLAB's names alone, and a uipanel has no Name (ADR 0199)",
        ["UiPanelModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it (ADR 0199)",
        ["UiPanelModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0199)",
        ["UiButtonGroupModel.Name"] = "a component answers to MATLAB's names alone, and a uibuttongroup has no Name (ADR 0200)",
        ["UiButtonGroupModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it (ADR 0200)",
        ["UiButtonGroupModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0200)",
        ["UiProgressIndicatorModel.Name"] = "a component answers to MATLAB's names alone, and a progress indicator has no Name (ADR 0201)",
        ["UiProgressIndicatorModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it (ADR 0201)",
        ["UiProgressIndicatorModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0201)",
        ["UiComponentModel.Name"] = "a uifigure component answers to R2025b's names alone, and none has a Name (ADR 0202)",
        ["UiComponentModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0202)",
        ["UiComponentModel.Selectable"] = "the model's own; R2025b's components have no HitTest to carry it (ADR 0202)",
        ["UiGridLayoutModel.Name"] = "a grid answers to R2025b's names alone, and has no Name (ADR 0202)",
        ["UiGridLayoutModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0202)",
        ["UiGridLayoutModel.Selectable"] = "the model's own; R2025b's grid has no HitTest to carry it (ADR 0202)",
        ["UiOverlayModel.Name"] = "a dialog over a figure has R2025b's ten ProgressDialog properties and no others (ADR 0202)",
        ["UiOverlayModel.Visible"] = "a dialog over a figure has R2025b's ten ProgressDialog properties and no others (ADR 0202)",
        ["UiOverlayModel.ZOrder"] = "a dialog over a figure has R2025b's ten ProgressDialog properties and no others (ADR 0202)",
        ["UiOverlayModel.Selectable"] = "a dialog over a figure has R2025b's ten ProgressDialog properties and no others (ADR 0202)",
        ["ContextMenuModel.Items"] = "reached as Children",
        ["MenuItemModel.Items"] = "reached as Children",
        ["ContextMenuModel.Name"] = "a context menu answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["ContextMenuModel.ZOrder"] = "a menu's place is its Position among its siblings (ADR 0206)",
        ["ContextMenuModel.Selectable"] = "reached as HitTest, which R2025b's context menu answers though it lists none (ADR 0206)",
        ["MenuItemModel.Name"] = "a menu answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["MenuItemModel.ZOrder"] = "a menu's place is its Position among its siblings (ADR 0206)",
        ["MenuItemModel.Selectable"] = "reached as HitTest, which R2025b's menu answers though it lists none (ADR 0206)",
        ["UiToolbarModel.Name"] = "a toolbar answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["UiToolbarModel.ZOrder"] = "a toolbar's place is its place among its figure's children (ADR 0206)",
        ["UiToolbarModel.Selectable"] = "reached as HitTest, which R2025b's toolbar answers though it lists none (ADR 0206)",
        ["UiToolModel.Name"] = "a toolbar tool answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["UiToolModel.ZOrder"] = "a tool's place is its place among its toolbar's children (ADR 0206)",
        ["UiToolModel.Selectable"] = "reached as HitTest, which R2025b's tool answers though it lists none (ADR 0206)",
        ["UiToggleToolModel.Name"] = "a toolbar tool answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["UiToggleToolModel.ZOrder"] = "a tool's place is its place among its toolbar's children (ADR 0206)",
        ["UiToggleToolModel.Selectable"] = "reached as HitTest, which R2025b's tool answers though it lists none (ADR 0206)",
        ["UiTabGroupModel.Name"] = "a tab group answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["UiTabGroupModel.ZOrder"] = "a component's stacking is its place among Children, as uistack moves it (ADR 0206)",
        ["UiTabGroupModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it (ADR 0206)",
        ["UiTabModel.Name"] = "a tab answers to R2025b's names alone, and has no Name (ADR 0206)",
        ["UiTabModel.ZOrder"] = "a tab's place is its place among its group's Children (ADR 0206)",
        ["UiTabModel.Selectable"] = "reached as HitTest, which is MATLAB's name for it (ADR 0206)",
        ["UiTabModel.Visible"] = "R2025b's tab has no Visible: its group shows it or does not (ADR 0206, fixture u8_tabs)",
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
            if (Excused.ContainsKey(key) || Excused.ContainsKey($"{type.Name}.{info.Name}")
                || (typeof(UiComponentModel).IsAssignableFrom(type) && Excused.ContainsKey($"{nameof(UiComponentModel)}.{info.Name}")))
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
        // R2025b's own exceptions (ADR 0202, fixtures u5_props and u5_dialogs): a ProgressDialog has
        // its ten properties and none of these, and a uifigure component that holds nothing has no
        // Children.
        if (type == typeof(UiOverlayModel))
        {
            return;
        }

        foreach (string universal in new[] { "type", "tag", "userdata", "parent", "children", "visible" })
        {
            // A table has Children in R2025b, an empty; no other component of a uifigure has.
            if (universal == "children" && typeof(UiComponentModel).IsAssignableFrom(type) && type != typeof(UiTableModel))
            {
                continue;
            }

            // R2025b's tab has no Visible (ADR 0206, fixture u8_tabs).
            if (universal == "visible" && type == typeof(UiTabModel))
            {
                continue;
            }

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
