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
    public void TheU9Kinds_KeepWhatTheyHold_AndATreeItsNodes()
    {
        var figure = new FigureModel { IsUiFigure = true };
        figure.Components.Add(new UiKnobModel { Lower = 10, Upper = 50, Value = 30, MajorTicks = [10, 30, 50], MajorTicksManual = true, Tag = "k" });
        figure.Components.Add(new UiSemicircularGaugeModel
        {
            Value = 7,
            ScaleColors = [new UiColor(1, 0, 0), new UiColor(0, 1, 0)],
            ScaleColorLimits = [0, 50, 50, 100],
            ScaleColorLimitsManual = true,
            Orientation = "south",
        });
        figure.Components.Add(new UiSwitchModel(UiSwitchStyle.Rocker) { Items = ["Lo", "Hi"], Selected = [1] });
        figure.Components.Add(new UiLampModel { Color = new UiColor(1, 0, 0) });
        figure.Components.Add(new UiDatePickerModel
        {
            ValueDays = 45306,
            DisplayFormat = "uuuu-MM-dd",
            DisabledDays = [45300, 45301],
            DisabledWeekdays = [1, 7],
            LowerDays = 45000,
            UpperDays = 46000,
            Editable = false,
            Placeholder = "pick",
        });
        figure.Components.Add(new UiColorPickerModel { Value = new UiColor(0, 0, 1) });
        var tree = new UiCheckBoxTreeModel { Multiselect = true, Editable = true };
        var root = new UiTreeNodeModel { Text = "root", Expanded = true };
        root.Nodes.Add(new UiTreeNodeModel { Text = "leaf" });
        tree.Nodes.Add(root);
        tree.Nodes.Add(new UiTreeNodeModel { Text = "other" });
        figure.Components.Add(tree);

        FigureModel loaded = RoundTrip(figure);

        var knob = Assert.IsType<UiKnobModel>(loaded.Components[0]);
        Assert.Equal((10, 50, 30, "k"), (knob.Lower, knob.Upper, knob.Value, knob.Tag));
        Assert.Equal([10, 30, 50], knob.MajorTicks);
        Assert.True(knob.MajorTicksManual);
        var gauge = Assert.IsType<UiSemicircularGaugeModel>(loaded.Components[1]);
        Assert.Equal(7, gauge.Value);
        Assert.Equal([new UiColor(1, 0, 0), new UiColor(0, 1, 0)], gauge.ScaleColors);
        Assert.Equal([0, 50, 50, 100], gauge.ScaleColorLimits);
        Assert.Equal(("south", true), (gauge.Orientation, gauge.ScaleColorLimitsManual));
        var toggle = Assert.IsType<UiSwitchModel>(loaded.Components[2]);
        Assert.Equal((UiSwitchStyle.Rocker, true), (toggle.Style, toggle.IsOn));
        Assert.Equal(["Lo", "Hi"], toggle.Items);
        Assert.Equal(new UiColor(1, 0, 0), Assert.IsType<UiLampModel>(loaded.Components[3]).Color);
        var picker = Assert.IsType<UiDatePickerModel>(loaded.Components[4]);
        Assert.Equal((45306.0, "uuuu-MM-dd", 45000.0, 46000.0, false, "pick"),
            (picker.ValueDays, picker.DisplayFormat, picker.LowerDays, picker.UpperDays, picker.Editable, picker.Placeholder));
        Assert.Equal([45300, 45301], picker.DisabledDays);
        Assert.Equal([1, 7], picker.DisabledWeekdays);
        Assert.Equal(new UiColor(0, 0, 1), Assert.IsType<UiColorPickerModel>(loaded.Components[5]).Value);
        var boxes = Assert.IsType<UiCheckBoxTreeModel>(loaded.Components[6]);
        Assert.True(boxes.CheckBoxes && boxes.Multiselect && boxes.Editable);
        Assert.Equal(["root", "other"], boxes.Nodes.Select(static n => n.Text));
        Assert.True(boxes.Nodes[0].Expanded);
        Assert.Equal("leaf", Assert.Single(boxes.Nodes[0].Nodes).Text);
        Assert.Same(boxes, boxes.Nodes[0].Nodes[0].Tree);
    }

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
    public void ACustomComponentsArea_IsSavedAsTheBorderlessPanelItIs_WithWhatItHolds()
    {
        // U10 (ADR 0209): the class is the script's, so a saved figure keeps the picture - a
        // borderless panel and what the component's setup built in it.
        var figure = new FigureModel { IsUiFigure = true, IntegerHandle = false };
        var area = new UiComponentContainerModel("SpinnerGauge") { Position = new Rect2D(10, 10, 220, 80) };
        area.Components.Add(new UiControlModel { Tag = "inside" });
        figure.Components.Add(area);

        FigureModel loaded = RoundTrip(figure);
        var panel = Assert.IsType<UiPanelModel>(Assert.Single(loaded.Components));
        Assert.Equal(UiBorderType.None, panel.BorderType);
        Assert.Equal(new Rect2D(10, 10, 220, 80), panel.Position);
        Assert.Equal(UiUnits.Pixels, panel.Units);
        Assert.Equal("inside", Assert.Single(panel.Components).Tag);
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

    // --- the uifigure components and uigridlayout (U5, ADR 0202) -------------------------------------

    [Fact]
    public void AUifigureComponent_KeepsWhatItHolds()
    {
        var figure = new FigureModel();
        figure.Components.Add(new UiSliderModel { Lower = -5, Upper = 5, Value = 2.5, Vertical = true, Tag = "s", Position = new Rect2D(10, 20, 3, 150) });
        figure.Components.Add(new UiDropDownModel { Items = ["Red", "Green", "Blue"], Selected = [2], FontSize = 14, Bold = true });
        figure.Components.Add(new UiNumericEditFieldModel { Value = null, AllowEmpty = true, Lower = double.NegativeInfinity, Upper = 10, UpperInclusive = false });
        figure.Components.Add(new UiLabelModel
        {
            Text = new UiText(UiTextForm.Cell, ["two", "lines"]),
            FontColor = new UiColor(0.2, 0.4, 0.6),
            BackgroundColor = new UiColor(1, 1, 0),
            HorizontalAlignment = UiHorizontalAlignment.Right,
            VerticalAlignment = UiVerticalAlignment.Top,
        });
        figure.Components.Add(new UiImageModel { Image = new UiImage(1, 2, [1, 2, 3, 4, 5, 6, 7, 8]), ScaleMethod = "fill" });
        figure.Components.Add(new UiTextAreaModel { Lines = ["a", "", "c"], Editable = false });

        FigureModel loaded = RoundTrip(figure);

        var slider = Assert.IsType<UiSliderModel>(loaded.Components[0]);
        Assert.Equal((-5, 5, 2.5, true, "s"), (slider.Lower, slider.Upper, slider.Value, slider.Vertical, slider.Tag));
        Assert.Equal(new Rect2D(10, 20, 3, 150), slider.Position);
        var list = Assert.IsType<UiDropDownModel>(loaded.Components[1]);
        Assert.Equal(["Red", "Green", "Blue"], list.Items);
        Assert.Equal([2], list.Selected);
        Assert.Equal((14, true), (list.FontSize, list.Bold));
        var number = Assert.IsType<UiNumericEditFieldModel>(loaded.Components[2]);
        Assert.Null(number.Value);
        Assert.Equal((true, double.NegativeInfinity, 10, false), (number.AllowEmpty, number.Lower, number.Upper, number.UpperInclusive));
        var label = Assert.IsType<UiLabelModel>(loaded.Components[3]);
        Assert.Equal(UiTextForm.Cell, label.Text.Form);
        Assert.Equal(["two", "lines"], label.Text.Lines);
        Assert.Equal(new UiColor(0.2, 0.4, 0.6), label.FontColor);
        Assert.Equal(new UiColor(1, 1, 0), label.BackgroundColor);
        Assert.Equal((UiHorizontalAlignment.Right, UiVerticalAlignment.Top), (label.HorizontalAlignment, label.VerticalAlignment));
        var picture = Assert.IsType<UiImageModel>(loaded.Components[4]);
        Assert.Equal((1, 2), (picture.Image!.Width, picture.Image.Height));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, picture.Image.Bgra);
        Assert.Equal("fill", picture.ScaleMethod);
        var area = Assert.IsType<UiTextAreaModel>(loaded.Components[5]);
        Assert.Equal(["a", "", "c"], area.Lines);
        Assert.False(area.Editable);
    }

    [Fact]
    public void AGrid_KeepsItsTracksItsChildrenAndTheirCells_AndAUiaxesInItFindsItsCell()
    {
        var figure = new FigureModel { IsUiFigure = true };
        var grid = new UiGridLayoutModel
        {
            Rows = [new UiGridTrack(UiGridTrackKind.Fixed, 40), UiGridTrack.Fit, new UiGridTrack(UiGridTrackKind.Weight, 2)],
            Columns = [UiGridTrack.Fit, UiGridTrack.One],
            Padding = [1, 2, 3, 4],
            RowSpacing = 5,
            ColumnSpacing = 6,
        };
        figure.Components.Add(grid);
        grid.Components.Add(new UiButtonModel { Text = UiText.Of("Go"), GridCell = new UiGridCell(1, 1, 1, 2) });
        var inner = new UiPanelModel { GridCell = new UiGridCell(3, 1) };
        grid.Components.Add(inner);
        inner.Components.Add(new UiCheckBoxModel { Value = true });
        var axes = new AxesModel { Container = grid, GridCell = new UiGridCell(2, 2), ReplaceChildrenOnly = true, PositionIsOuter = true };
        figure.Axes.Add(axes);

        FigureModel loaded = RoundTrip(figure);

        var again = Assert.IsType<UiGridLayoutModel>(Assert.Single(loaded.Components));
        Assert.Equal(grid.Rows, again.Rows);
        Assert.Equal(grid.Columns, again.Columns);
        Assert.Equal([1, 2, 3, 4], again.Padding);
        Assert.Equal((5, 6), (again.RowSpacing, again.ColumnSpacing));
        var button = Assert.IsType<UiButtonModel>(again.Components[0]);
        Assert.Equal("Go", button.Text.Joined);
        Assert.Equal(new UiGridCell(1, 1, 1, 2), button.GridCell);
        var panel = Assert.IsType<UiPanelModel>(again.Components[1]);
        Assert.Equal(new UiGridCell(3, 1), panel.GridCell);
        Assert.True(Assert.IsType<UiCheckBoxModel>(Assert.Single(panel.Components)).Value);
        AxesModel plotted = Assert.Single(loaded.Axes);
        Assert.Same(again, plotted.Container);
        Assert.Equal(new UiGridCell(2, 2), plotted.GridCell);
        Assert.True(plotted.ReplaceChildrenOnly);
        Assert.True(plotted.PositionIsOuter);
    }

    [Fact]
    public void AButtonGroupOfRadioButtons_KeepsTheOneSelected()
    {
        var figure = new FigureModel { IsUiFigure = true };
        var group = new UiButtonGroupModel();
        figure.Components.Add(group);
        group.Components.Add(new UiRadioButtonModel { Text = UiText.Of("A"), Value = false });
        group.Components.Add(new UiRadioButtonModel { Text = UiText.Of("B"), Value = true });

        var again = Assert.IsType<UiButtonGroupModel>(Assert.Single(RoundTrip(figure).Components));
        Assert.Equal([false, true], again.Components.Cast<UiRadioButtonModel>().Select(static b => b.Value));
    }

    [Fact]
    public void ATableATabGroupAndAFiguresBars_KeepWhatTheyHold()
    {
        // U8 (ADR 0206): a table keeps its picture, a tab group its tabs and the one that shows,
        // a figure its menu bar and its toolbars with their tools.
        var figure = new FigureModel { IsUiFigure = true };
        var group = UiTabGroupModel.ForUiFigure();
        group.TabLocation = UiTabLocation.Left;
        group.Tag = "tg";
        var first = new UiTabModel { Title = "One" };
        var second = new UiTabModel { Title = "Two", BackgroundColor = null, ForegroundColor = new UiColor(1, 0, 0) };
        group.Components.Add(first);
        group.Components.Add(second);
        group.Select(second);
        var table = new UiTableModel
        {
            Tag = "t",
            Units = UiUnits.Normalized,
            Position = new Rect2D(0, 0, 1, 1),
            Content = new UiTableContent(
                2,
                [new UiTableColumn("N", new UiGridTrack(UiGridTrackKind.Fixed, 50), true, false), new UiTableColumn("L", new UiGridTrack(UiGridTrackKind.Fit, 0), false, true, ["a", "b"])],
                [new UiTableCell("1.5000", UiTableCellKind.Number), new UiTableCell(string.Empty, UiTableCellKind.Checked),
                 new UiTableCell("2", UiTableCellKind.Number), new UiTableCell("txt", UiTableCellKind.Text)],
                ["r1", "r2"]),
            Stripes = [new UiColor(1, 0, 0), new UiColor(0, 1, 0), new UiColor(0, 0, 1)],
            RowStriping = false,
            SelectionType = UiTableSelectionType.Row,
            Multiselect = false,
            Selection = [1],
            FontUnits = UiFontUnits.Points,
            FontSize = 9,
        };
        second.Components.Add(table);
        figure.Components.Add(group);

        var file = new MenuItemModel { Text = "&File", Tag = "mf" };
        file.Items.Add(new MenuItemModel { Text = "Open", Accelerator = "O", Checked = true });
        file.Items.Add(new MenuItemModel { Text = "Hidden", Visible = false, Separator = true, Enable = false });
        figure.Menus.Add(file);
        var bar = new UiToolbarModel { Tag = "tb", BackgroundColor = new UiColor(0.5, 0.6, 0.7) };
        bar.Tools.Add(new UiToolModel { Tooltip = UiText.Of("go"), Picture = new UiImage(1, 2, [1, 2, 3, 255, 4, 5, 6, 255]), Separator = true });
        bar.Tools.Add(new UiToggleToolModel { State = true, Enable = false, Tag = "tt", Visible = false });
        figure.Toolbars.Add(bar);

        FigureModel loaded = RoundTrip(figure);

        var loadedGroup = Assert.IsType<UiTabGroupModel>(Assert.Single(loaded.Components));
        Assert.Equal(UiTabLocation.Left, loadedGroup.TabLocation);
        Assert.Equal("tg", loadedGroup.Tag);
        Assert.Equal(["One", "Two"], loadedGroup.Tabs.Select(static tab => tab.Title));
        UiTabModel showing = loadedGroup.SelectedTab!;
        Assert.Equal("Two", showing.Title);
        Assert.Null(showing.BackgroundColor);
        Assert.Equal(new UiColor(1, 0, 0), showing.ForegroundColor);

        var loadedTable = Assert.IsType<UiTableModel>(Assert.Single(showing.Components));
        Assert.Equal(UiUnits.Normalized, loadedTable.Units);
        Assert.Equal(2, loadedTable.Content.Rows);
        Assert.Equal(["N", "L"], loadedTable.Content.Columns.Select(static column => column.Header));
        Assert.Equal(new UiGridTrack(UiGridTrackKind.Fixed, 50), loadedTable.Content.Columns[0].Width);
        Assert.Equal(["a", "b"], loadedTable.Content.Columns[1].Choices);
        Assert.Equal(new UiTableCell("1.5000", UiTableCellKind.Number), loadedTable.Content.At(0, 0));
        Assert.Equal(UiTableCellKind.Checked, loadedTable.Content.At(0, 1).Kind);
        Assert.Equal(new UiTableCell("txt", UiTableCellKind.Text), loadedTable.Content.At(1, 1));
        Assert.Equal(["r1", "r2"], loadedTable.Content.RowHeaders);
        Assert.Equal(3, loadedTable.Stripes.Count);
        Assert.False(loadedTable.RowStriping);
        Assert.Equal(UiTableSelectionType.Row, loadedTable.SelectionType);
        Assert.False(loadedTable.Multiselect);
        Assert.Equal([1], loadedTable.Selection);
        Assert.Equal(UiFontUnits.Points, loadedTable.FontUnits);
        Assert.Equal(9, loadedTable.FontSize);

        MenuItemModel loadedFile = Assert.Single(loaded.Menus);
        Assert.Equal("&File", loadedFile.Text);
        Assert.Equal("mf", loadedFile.Tag);
        Assert.Equal(["Open", "Hidden"], loadedFile.Items.Select(static item => item.Text));
        Assert.True(loadedFile.Items[0].Checked);
        Assert.Equal("O", loadedFile.Items[0].Accelerator);
        Assert.False(loadedFile.Items[1].Visible);
        Assert.True(loadedFile.Items[1].Separator);
        Assert.False(loadedFile.Items[1].Enable);

        UiToolbarModel loadedBar = Assert.Single(loaded.Toolbars);
        Assert.Equal("tb", loadedBar.Tag);
        Assert.Equal(new UiColor(0.5, 0.6, 0.7), loadedBar.BackgroundColor);
        Assert.Equal(2, loadedBar.Tools.Count);
        Assert.False(loadedBar.Tools[0].IsToggle);
        Assert.Equal("go", loadedBar.Tools[0].Tooltip.Joined);
        Assert.Equal((1, 2), (loadedBar.Tools[0].Picture!.Width, loadedBar.Tools[0].Picture!.Height));
        Assert.Equal<byte>([1, 2, 3, 255, 4, 5, 6, 255], loadedBar.Tools[0].Picture!.Bgra);
        Assert.True(loadedBar.Tools[0].Separator);
        Assert.True(loadedBar.Tools[1].IsToggle);
        Assert.True(loadedBar.Tools[1].State);
        Assert.False(loadedBar.Tools[1].Enable);
        Assert.False(loadedBar.Tools[1].Visible);
        Assert.Equal("tt", loadedBar.Tools[1].Tag);
    }
}
