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
