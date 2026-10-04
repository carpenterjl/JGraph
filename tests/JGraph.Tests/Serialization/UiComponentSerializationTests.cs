using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// App-building components in the document format (app-building plan, decision Q9; ADR 0199): what
/// a script could read back of a <c>uicontrol</c> and a <c>uipanel</c> survives a save, panels keep
/// what is in them, and an axes placed in a panel finds its panel again.
/// </summary>
public class UiComponentSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void AUicontrol_KeepsWhatAScriptSet_AndWhatItLeftToFollowTheStyle()
    {
        var figure = new FigureModel();
        figure.Components.Add(new UiControlModel
        {
            Style = UiControlStyle.Edit,
            Text = new UiText(UiTextForm.Cell, ["one", "two"]),
            Units = UiUnits.Normalized,
            Position = new Rect2D(0.1, 0.2, 0.3, 0.4),
            ForegroundColor = new UiColor(0, 0.45, 0.74),
            FontSize = 10,
            FontWeight = "bold",
            HorizontalAlignment = UiHorizontalAlignment.Left,
            Enable = UiEnable.Inactive,
            Tooltip = UiText.Of("tip"),
            Tag = "field",
            Visible = false,
        });
        figure.Components.Add(new UiControlModel
        {
            Style = UiControlStyle.ListBox,
            Value = new UiNumbers([1, 3], 1, 2),
            BackgroundColor = null,
            ForegroundColor = null,
        });

        FigureModel loaded = RoundTrip(figure);
        var edit = Assert.IsType<UiControlModel>(loaded.Components[0]);
        Assert.Equal(UiControlStyle.Edit, edit.Style);
        Assert.Equal(UiTextForm.Cell, edit.Text.Form);
        Assert.Equal(["one", "two"], edit.Text.Lines);
        Assert.Equal(UiUnits.Normalized, edit.Units);
        Assert.Equal(new Rect2D(0.1, 0.2, 0.3, 0.4), edit.Position);
        Assert.Equal(new UiColor(0, 0.45, 0.74), edit.ForegroundColor);
        Assert.Equal(10, edit.FontSize);
        Assert.Equal("bold", edit.FontWeight);
        Assert.Equal(UiHorizontalAlignment.Left, edit.HorizontalAlignment);
        Assert.Equal(UiEnable.Inactive, edit.Enable);
        Assert.Equal("tip", edit.Tooltip.Joined);
        Assert.Equal("field", edit.Tag);
        Assert.False(edit.Visible);

        // Never set, so they still follow the style — and would follow a new one.
        Assert.False(edit.BackgroundIsSet);
        Assert.False(edit.ValueIsSet);
        Assert.Equal(new UiColor(1, 1, 1), edit.BackgroundColor);
        edit.Style = UiControlStyle.PushButton;
        Assert.Equal(UiControlModel.DefaultBackground, edit.BackgroundColor);

        // Set, to 'none' and to a selection: kept as set.
        var list = Assert.IsType<UiControlModel>(loaded.Components[1]);
        Assert.True(list.BackgroundIsSet);
        Assert.Null(list.BackgroundColor);
        Assert.Null(list.ForegroundColor);
        Assert.Equal([1.0, 3.0], list.Value.Data);
        Assert.Equal(2, list.Value.Columns);
    }

    [Fact]
    public void APanel_KeepsItsLook_ItsChildren_AndTheAxesPlacedInIt()
    {
        var figure = new FigureModel { IsUiFigure = true, IntegerHandle = false, AutoResizeChildren = true, Units = UiUnits.Points };
        var outer = new UiPanelModel
        {
            Units = UiUnits.Pixels,
            Position = new Rect2D(50, 60, 300, 250),
            Title = UiText.Of("Outer"),
            TitlePosition = UiTitlePosition.CenterTop,
            BorderType = UiBorderType.EtchedIn,
            BorderWidth = 2,
            BackgroundColor = new UiColor(0.9, 0.8, 0.7),
            BorderColor = null,
            FontUnits = UiFontUnits.Pixels,
            FontSize = 12,
            Scrollable = true,
        };
        var inner = new UiPanelModel { AutoResizeChildren = true };
        inner.Components.Add(new UiControlModel { Tag = "deep" });
        outer.Components.Add(new UiControlModel { Tag = "first" });
        outer.Components.Add(inner);
        figure.Components.Add(new UiControlModel { Tag = "beside" });
        figure.Components.Add(outer);

        AxesModel free = figure.AddAxes();
        AxesModel held = figure.AddAxes();
        held.Container = inner;
        held.Units = UiUnits.Pixels;
        held.PixelBounds = new Rect2D(10, 20, 100, 80);
        held.InnerTarget = new Rect2D(0.1, 0.2, 0.3, 0.4);

        FigureModel loaded = RoundTrip(figure);
        Assert.True(loaded.IsUiFigure);
        Assert.False(loaded.IntegerHandle);
        Assert.True(loaded.AutoResizeChildren);
        Assert.Equal(UiUnits.Points, loaded.Units);

        Assert.Equal(2, loaded.Components.Count);
        var panel = Assert.IsType<UiPanelModel>(loaded.Components[1]);
        Assert.Equal("Outer", panel.Title.Joined);
        Assert.Equal(UiTitlePosition.CenterTop, panel.TitlePosition);
        Assert.Equal(UiBorderType.EtchedIn, panel.BorderType);
        Assert.Equal(2, panel.BorderWidth);
        Assert.Equal(new UiColor(0.9, 0.8, 0.7), panel.BackgroundColor);
        Assert.Null(panel.BorderColor);
        Assert.Equal(UiFontUnits.Pixels, panel.FontUnits);
        Assert.True(panel.Scrollable);
        Assert.Equal(new Rect2D(50, 60, 300, 250), panel.Position);
        Assert.Equal(outer.Insets(), panel.Insets());

        Assert.Equal("first", panel.Components[0].Tag);
        var nested = Assert.IsType<UiPanelModel>(panel.Components[1]);
        Assert.True(nested.AutoResizeChildren);
        Assert.Equal(UiUnits.Normalized, nested.Units);
        Assert.Equal("deep", Assert.Single(nested.Components).Tag);
        Assert.Same(loaded, nested.Figure);

        Assert.Null(loaded.Axes[0].Container);
        Assert.Same(nested, loaded.Axes[1].Container);
        Assert.Equal(UiUnits.Pixels, loaded.Axes[1].Units);
        Assert.Equal(new Rect2D(10, 20, 100, 80), loaded.Axes[1].PixelBounds);
        Assert.Equal(UiUnits.Normalized, loaded.Axes[0].Units);
        Assert.Null(loaded.Axes[0].PixelBounds);
    }

    [Fact]
    public void AFigureWithNoComponents_WritesNoneAndLoadsAsItAlwaysDid()
    {
        FigureModel loaded = RoundTrip(new FigureModel());
        Assert.Empty(loaded.Components);
        Assert.False(loaded.IsUiFigure);
        Assert.True(loaded.IntegerHandle);
        Assert.Equal(UiUnits.Pixels, loaded.Units);
    }
}
