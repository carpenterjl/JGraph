using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction.Editing;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Editing;

public class EditablePropertyFactoryTests
{
    private static EditableProperty Find(IReadOnlyList<EditableProperty> properties, string name) =>
        Assert.Single(properties, p => p.Name == name);

    [Fact]
    public void Describe_MapsTypesToEditors()
    {
        IReadOnlyList<EditableProperty> properties =
            EditablePropertyFactory.Describe(new LinePlot(new double[] { 0 }, new double[] { 0 }));

        Assert.Equal(PropertyEditorKind.Text, Find(properties, "Name").Editor);
        Assert.Equal(PropertyEditorKind.Toggle, Find(properties, "Visible").Editor);
        Assert.Equal(PropertyEditorKind.Number, Find(properties, "LineWidth").Editor);
        Assert.Equal(PropertyEditorKind.Enum, Find(properties, "DashStyle").Editor);
        Assert.Equal(PropertyEditorKind.OptionalColor, Find(properties, "Color").Editor);
    }

    [Fact]
    public void Describe_HidesBrowsableFalseAndUnsupportedProperties()
    {
        IReadOnlyList<EditableProperty> properties =
            EditablePropertyFactory.Describe(new LinePlot(new double[] { 0 }, new double[] { 0 }));

        Assert.DoesNotContain(properties, p => p.Name == nameof(GraphObject.Id));         // [Browsable(false)]
        Assert.DoesNotContain(properties, p => p.Name == nameof(GraphObject.IsSelected)); // [Browsable(false)]
        Assert.DoesNotContain(properties, p => p.Name == nameof(GraphObject.Parent));     // read-only
        Assert.DoesNotContain(properties, p => p.Name == nameof(XYPlot.Data));            // unsupported type
    }

    [Fact]
    public void Describe_UsesAttributesForNamesAndCategories()
    {
        IReadOnlyList<EditableProperty> properties =
            EditablePropertyFactory.Describe(new LinePlot(new double[] { 0 }, new double[] { 0 }));

        EditableProperty lineWidth = Find(properties, "LineWidth");
        Assert.Equal("Line width", lineWidth.DisplayName);
        Assert.Equal("Appearance", lineWidth.Category);
        Assert.Equal("General", Find(properties, "Name").Category);
    }

    [Fact]
    public void Describe_OrdersGeneralCategoryFirst()
    {
        IReadOnlyList<EditableProperty> properties =
            EditablePropertyFactory.Describe(new LinePlot(new double[] { 0 }, new double[] { 0 }));

        Assert.Equal("General", properties[0].Category);
    }

    [Fact]
    public void Describe_AxisModel_ExposesRangeEditor()
    {
        var axis = new AxesModel().PrimaryXAxis;
        IReadOnlyList<EditableProperty> properties = EditablePropertyFactory.Describe(axis);

        Assert.Equal(PropertyEditorKind.Range, Find(properties, "Range").Editor);
        Assert.DoesNotContain(properties, p => p.Name == nameof(AxisModel.DataBounds));
    }

    [Fact]
    public void TryParse_Number_AcceptsValidRejectsInvalid()
    {
        EditableProperty lineWidth = Find(
            EditablePropertyFactory.Describe(typeof(LinePlot)), "LineWidth");

        Assert.True(EditablePropertyFactory.TryParse(lineWidth, "2.5", out object? value));
        Assert.Equal(2.5, value);
        Assert.False(EditablePropertyFactory.TryParse(lineWidth, "abc", out _));
    }

    [Fact]
    public void TryParse_Int_RejectsFractions()
    {
        EditableProperty zOrder = Find(EditablePropertyFactory.Describe(typeof(LinePlot)), "ZOrder");

        Assert.True(EditablePropertyFactory.TryParse(zOrder, "3", out object? value));
        Assert.Equal(3, value);
        Assert.False(EditablePropertyFactory.TryParse(zOrder, "3.5", out _));
    }

    [Fact]
    public void TryParse_OptionalColor_SupportsAutoAndHex()
    {
        EditableProperty color = Find(EditablePropertyFactory.Describe(typeof(LinePlot)), "Color");

        Assert.True(EditablePropertyFactory.TryParse(color, "auto", out object? auto));
        Assert.Null(auto);
        Assert.True(EditablePropertyFactory.TryParse(color, "#FF0000", out object? red));
        Assert.Equal(Colors.Red, Assert.IsType<Color>(red));
        Assert.False(EditablePropertyFactory.TryParse(color, "#GGGGGG", out _));
    }

    [Fact]
    public void TryParse_Enum_IsCaseInsensitiveAndValidated()
    {
        EditableProperty dash = Find(EditablePropertyFactory.Describe(typeof(LinePlot)), "DashStyle");

        Assert.True(EditablePropertyFactory.TryParse(dash, "dash", out object? value));
        Assert.Equal(DashStyle.Dash, value);
        Assert.False(EditablePropertyFactory.TryParse(dash, "wavy", out _));
    }

    [Fact]
    public void TryParse_Range_ParsesMinMaxPair()
    {
        EditableProperty range = Find(EditablePropertyFactory.Describe(typeof(AxisModel)), "Range");

        Assert.True(EditablePropertyFactory.TryParse(range, "-2, 7", out object? value));
        Assert.Equal(new DataRange(-2, 7), value);
        Assert.False(EditablePropertyFactory.TryParse(range, "5", out _));
    }

    [Fact]
    public void Format_RoundTripsThroughTryParse()
    {
        EditableProperty range = Find(EditablePropertyFactory.Describe(typeof(AxisModel)), "Range");
        string text = EditablePropertyFactory.Format(range, new DataRange(1.5, 4.25));
        Assert.True(EditablePropertyFactory.TryParse(range, text, out object? value));
        Assert.Equal(new DataRange(1.5, 4.25), value);

        EditableProperty color = Find(EditablePropertyFactory.Describe(typeof(LinePlot)), "Color");
        Assert.Equal("#FF0000", EditablePropertyFactory.Format(color, Colors.Red));
    }

    [Fact]
    public void Describe_ExpandsTextStyleIntoHeaderAndMembers()
    {
        var axes = new AxesModel();
        IReadOnlyList<EditableProperty> properties = EditablePropertyFactory.Describe(axes);

        EditableProperty[] style = properties
            .Where(p => p.Name == nameof(AxesModel.TitleStyle))
            .ToArray();

        Assert.Equal(6, style.Length);
        Assert.True(style[0].IsHeader);
        Assert.Null(style[0].Group);
        Assert.Equal(
            new[] { "Font", "Font size", "Bold", "Italic", "Color" },
            style.Skip(1).Select(p => p.DisplayName));

        // Children carry the root's category (so they sort with it) and the header's name as their group.
        Assert.All(style, p => Assert.Equal("General", p.Category));
        Assert.All(style.Skip(1), p => Assert.Equal("Title style", p.Group));

        Assert.Equal(PropertyEditorKind.FontFamily, style[1].Editor);
        Assert.Equal(typeof(string), style[1].ValueType);
        Assert.Equal(PropertyEditorKind.Color, style[5].Editor);
        Assert.Equal(typeof(Color), style[5].ValueType);
    }

    [Fact]
    public void Describe_CompositeChild_ReadsAndRebuildsLeavingSiblingsIntact()
    {
        var axes = new AxesModel { TitleStyle = new TextStyle(Colors.Red, 15, "Consolas", bold: true) };
        EditableProperty size = Assert.Single(
            EditablePropertyFactory.Describe(axes),
            p => p.Name == nameof(AxesModel.TitleStyle) && p.DisplayName == "Font size");

        Assert.Equal(15d, size.GetValue(axes));

        size.SetValue(axes, 22d);

        Assert.Equal(22, axes.TitleStyle.FontSize);
        Assert.Equal("Consolas", axes.TitleStyle.FontFamily);
        Assert.True(axes.TitleStyle.Bold);
        Assert.Equal(Colors.Red, axes.TitleStyle.Color);
    }

    [Fact]
    public void Describe_CompositeChild_RecordsTheWholeStructForUndo()
    {
        var axes = new AxesModel { TitleStyle = new TextStyle(Colors.Black, 12, "Segoe UI") };
        EditableProperty bold = Assert.Single(
            EditablePropertyFactory.Describe(axes),
            p => p.Name == nameof(AxesModel.TitleStyle) && p.DisplayName == "Bold");

        // The undo path records the root property, so the whole style is restored in one step.
        Assert.Equal(nameof(AxesModel.TitleStyle), bold.Name);
        Assert.IsType<TextStyle>(bold.GetRootValue(axes));
    }

    [Fact]
    public void Describe_CompositeChild_TakesItsEnumValuesFromTheMemberNotTheComposite()
    {
        var grid = new AxesModel().Grid;
        EditableProperty dash = Assert.Single(
            EditablePropertyFactory.Describe(grid),
            p => p.Name == nameof(GridModel.MajorLineStyle) && p.DisplayName == "Dash");

        // Reading the enum's values off Property.PropertyType would yield LineStyle and throw.
        Assert.Equal(typeof(DashStyle), dash.ValueType);
        Assert.True(EditablePropertyFactory.TryParse(dash, "dot", out object? parsed));
        Assert.Equal(DashStyle.Dot, parsed);

        dash.SetValue(grid, DashStyle.Dot);
        Assert.Equal(DashStyle.Dot, grid.MajorLineStyle.Dash);
    }

    [Fact]
    public void Describe_CompositeChild_FormatRoundTripsThroughTryParse()
    {
        var axes = new AxesModel();
        EditableProperty font = Assert.Single(
            EditablePropertyFactory.Describe(axes),
            p => p.Name == nameof(AxesModel.TitleStyle) && p.DisplayName == "Font");

        string text = EditablePropertyFactory.Format(font, axes.TitleStyle.FontFamily);
        Assert.True(EditablePropertyFactory.TryParse(font, text, out object? value));
        Assert.Equal(axes.TitleStyle.FontFamily, value);
        Assert.False(EditablePropertyFactory.TryParse(font, "  ", out _));
    }

    [Fact]
    public void Describe_LeavesNonCompositeTypesFlat()
    {
        // Marker styling is exposed as flat properties on the plot, so nothing here expands: a
        // MarkerStyle entry in the composite table would be dead code.
        IReadOnlyList<EditableProperty> properties =
            EditablePropertyFactory.Describe(typeof(LinePlot));

        Assert.DoesNotContain(properties, p => p.IsHeader);
        Assert.All(properties, p => Assert.Null(p.Group));
        Assert.Equal(PropertyEditorKind.Number, Find(properties, "MarkerSize").Editor);
    }

    [Fact]
    public void PropertiesWithoutAttributes_GetHumanizedNamesAndDefaultCategory()
    {
        // TestPlot's DataRange properties carry no ComponentModel attributes at all.
        IReadOnlyList<EditableProperty> properties = EditablePropertyFactory.Describe(
            typeof(TestDoubles.TestPlot));

        EditableProperty xBounds = Find(properties, "XBoundsValue");
        Assert.Equal("XBounds value", xBounds.DisplayName); // capital runs stay together
        Assert.Equal("Other", xBounds.Category);
        Assert.Equal(PropertyEditorKind.Range, xBounds.Editor);
    }
}
