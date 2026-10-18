classdef DesignerSpecApp < matlab.apps.AppBase
    % The designer plan's DS1 fixture: an app laid out as App Designer lays one out, but whose
    % createComponents was written by hand here and not by App Designer. Each component's properties
    % are set out of the generator's order, some to their defaults, and every uifigure component
    % class App Designer offers is made once or more. The probe ds1_generate runs these lines in
    % R2025b and records what R2025b's component code generator writes for each component
    % (DesignerSpecApp.generated.txt); the designer must write the same from this text. Every value
    % is one its component keeps as set: a component's own coercions (a rounded numeric value, a
    % column of disabled dates, a button group's one selection, a parent node checked with its only
    % child) are not the writer's to apply.

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure           matlab.ui.Figure
        FileMenu           matlab.ui.container.Menu
        OpenMenu           matlab.ui.container.Menu
        Toolbar            matlab.ui.container.Toolbar
        PushTool           matlab.ui.container.toolbar.PushTool
        ToggleTool         matlab.ui.container.toolbar.ToggleTool
        Grid               matlab.ui.container.GridLayout
        Axes               matlab.ui.control.UIAxes
        Table              matlab.ui.control.Table
        TabGroup           matlab.ui.container.TabGroup
        ControlsTab        matlab.ui.container.Tab
        GaugesTab          matlab.ui.container.Tab
        Panel              matlab.ui.container.Panel
        Button             matlab.ui.control.Button
        StateButton        matlab.ui.control.StateButton
        Label              matlab.ui.control.Label
        QuoteLabel         matlab.ui.control.Label
        CheckBox           matlab.ui.control.CheckBox
        EditField          matlab.ui.control.EditField
        NumericEditField   matlab.ui.control.NumericEditField
        Spinner            matlab.ui.control.Spinner
        DropDown           matlab.ui.control.DropDown
        ListBox            matlab.ui.control.ListBox
        TextArea           matlab.ui.control.TextArea
        DatePicker         matlab.ui.control.DatePicker
        ColorPicker        matlab.ui.control.ColorPicker
        Hyperlink          matlab.ui.control.Hyperlink
        HTML               matlab.ui.control.HTML
        Image              matlab.ui.control.Image
        ButtonGroup        matlab.ui.container.ButtonGroup
        RadioButton        matlab.ui.control.RadioButton
        RadioButton2       matlab.ui.control.RadioButton
        ToggleGroup        matlab.ui.container.ButtonGroup
        ToggleButton       matlab.ui.control.ToggleButton
        Slider             matlab.ui.control.Slider
        RangeSlider        matlab.ui.control.RangeSlider
        Gauge              matlab.ui.control.Gauge
        LinearGauge        matlab.ui.control.LinearGauge
        NinetyDegreeGauge  matlab.ui.control.NinetyDegreeGauge
        SemicircularGauge  matlab.ui.control.SemicircularGauge
        Knob               matlab.ui.control.Knob
        DiscreteKnob       matlab.ui.control.DiscreteKnob
        Switch             matlab.ui.control.Switch
        RockerSwitch       matlab.ui.control.RockerSwitch
        ToggleSwitch       matlab.ui.control.ToggleSwitch
        Lamp               matlab.ui.control.Lamp
        Tree               matlab.ui.container.Tree
        Node1              matlab.ui.container.TreeNode
        Node2              matlab.ui.container.TreeNode
        CheckTree          matlab.ui.container.CheckBoxTree
        Check1             matlab.ui.container.TreeNode
        Check2             matlab.ui.container.TreeNode
        Check3             matlab.ui.container.TreeNode
        ContextMenu        matlab.ui.container.ContextMenu
        CopyMenu           matlab.ui.container.Menu
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Button pushed function: Button
        function ButtonPushed(app, event)
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Get the file path for locating images
            pathToMLAPP = fileparts(mfilename('fullpath'));

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Name = 'Designer spec';
            app.UIFigure.Resize = 'off';
            app.UIFigure.Position = [100 100 900 700];
            app.UIFigure.NumberTitle = 'off';
            app.UIFigure.Scrollable = 'off';
            app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);
            app.UIFigure.Tag = 'spec';

            % Create FileMenu
            app.FileMenu = uimenu(app.UIFigure);
            app.FileMenu.Text = 'File';
            app.FileMenu.Tooltip = 'The file menu';

            % Create OpenMenu
            app.OpenMenu = uimenu(app.FileMenu);
            app.OpenMenu.Text = 'Open...';
            app.OpenMenu.Accelerator = 'O';
            app.OpenMenu.MenuSelectedFcn = createCallbackFcn(app, @OpenMenuSelected, true);
            app.OpenMenu.Separator = 'on';
            app.OpenMenu.Checked = 'off';

            % Create Toolbar
            app.Toolbar = uitoolbar(app.UIFigure);
            app.Toolbar.Visible = 'on';

            % Create PushTool
            app.PushTool = uipushtool(app.Toolbar);
            app.PushTool.Tooltip = 'Push it';
            app.PushTool.ClickedCallback = createCallbackFcn(app, @PushToolClicked, true);

            % Create ToggleTool
            app.ToggleTool = uitoggletool(app.Toolbar);
            app.ToggleTool.State = 'on';
            app.ToggleTool.Separator = 'on';

            % Create Grid
            app.Grid = uigridlayout(app.UIFigure);
            app.Grid.RowHeight = {'fit', '1x', 40};
            app.Grid.ColumnWidth = {200, '1x'};
            app.Grid.Padding = [5 5 5 5];
            app.Grid.RowSpacing = 4;
            app.Grid.ColumnSpacing = 10;

            % Create Axes
            app.Axes = uiaxes(app.Grid);
            xlabel(app.Axes, 'Time (s)')
            title(app.Axes, 'Response')
            ylabel(app.Axes, {'Amplitude'; 'volts'})
            app.Axes.Toolbar.Visible = 'off';
            app.Axes.XLim = [0 10];
            app.Axes.XGrid = 'on';
            app.Axes.Box = 'on';
            app.Axes.Layout.Column = [1 2];
            app.Axes.Layout.Row = 2;
            app.Axes.Position = [10 10 300 200];

            % Create Table
            app.Table = uitable(app.Grid);
            app.Table.ColumnName = {'Name'; 'Value'};
            app.Table.RowName = {};
            app.Table.ColumnEditable = [false true];
            app.Table.ColumnSortable = false;
            app.Table.Layout.Row = 3;
            app.Table.Layout.Column = 2;
            app.Table.FontSize = 11;

            % Create TabGroup
            app.TabGroup = uitabgroup(app.Grid);
            app.TabGroup.TabLocation = 'left';
            app.TabGroup.Layout.Row = 1;
            app.TabGroup.Layout.Column = [1 2];

            % Create ControlsTab
            app.ControlsTab = uitab(app.TabGroup);
            app.ControlsTab.Title = 'Controls';
            app.ControlsTab.BackgroundColor = [0.9 0.95 1];
            app.ControlsTab.Scrollable = 'on';

            % Create Panel
            app.Panel = uipanel(app.ControlsTab);
            app.Panel.Title = 'Inputs';
            app.Panel.BorderType = 'none';
            app.Panel.FontWeight = 'bold';
            app.Panel.Position = [10 10 600 400];
            app.Panel.FontSize = 14;
            app.Panel.AutoResizeChildren = 'off';

            % Create Button
            app.Button = uibutton(app.Panel, 'push');
            app.Button.Text = 'Go';
            app.Button.ButtonPushedFcn = createCallbackFcn(app, @ButtonPushed, true);
            app.Button.Position = [10 360 100 23];
            app.Button.FontSize = 12;
            app.Button.Enable = 'on';
            app.Button.HorizontalAlignment = 'center';
            app.Button.WordWrap = 'on';
            app.Button.Tooltip = 'Starts the run';
            app.Button.BackgroundColor = [0.2 0.6 0.2];
            app.Button.FontColor = [1 1 1];
            app.Button.IconAlignment = 'right';

            % Create StateButton
            app.StateButton = uibutton(app.Panel, 'state');
            app.StateButton.Value = true;
            app.StateButton.Text = {'Hold'};
            app.StateButton.Position = [120 360 100 23];

            % Create Label
            app.Label = uilabel(app.Panel);
            app.Label.Text = ['Two' newline 'lines'];
            app.Label.WordWrap = 'on';
            app.Label.Position = [10 320 100 30];
            app.Label.FontAngle = 'italic';
            app.Label.VerticalAlignment = 'top';
            app.Label.Interpreter = 'html';

            % Create QuoteLabel
            app.QuoteLabel = uilabel(app.Panel);
            app.QuoteLabel.Text = {'It''s'};
            app.QuoteLabel.Position = [120 320 100 22];
            app.QuoteLabel.HorizontalAlignment = 'right';
            app.QuoteLabel.Visible = 'off';

            % Create CheckBox
            app.CheckBox = uicheckbox(app.Panel);
            app.CheckBox.Value = true;
            app.CheckBox.Text = 'Enabled';
            app.CheckBox.Position = [10 290 100 22];
            app.CheckBox.ValueChangedFcn = createCallbackFcn(app, @CheckBoxValueChanged, true);

            % Create EditField
            app.EditField = uieditfield(app.Panel, 'text');
            app.EditField.Value = 'hello';
            app.EditField.Placeholder = 'Type here';
            app.EditField.Position = [10 260 120 22];
            app.EditField.CharacterLimits = [0 20];
            app.EditField.ValueChangingFcn = createCallbackFcn(app, @EditFieldValueChanging, true);

            % Create NumericEditField
            app.NumericEditField = uieditfield(app.Panel, 'numeric');
            app.NumericEditField.Value = 4;
            app.NumericEditField.Limits = [0 Inf];
            app.NumericEditField.LowerLimitInclusive = 'off';
            app.NumericEditField.ValueDisplayFormat = '%.3f';
            app.NumericEditField.Position = [140 260 100 22];
            app.NumericEditField.RoundFractionalValues = 'on';

            % Create Spinner
            app.Spinner = uispinner(app.Panel);
            app.Spinner.Limits = [-Inf 100];
            app.Spinner.Step = 5;
            app.Spinner.Value = 15;
            app.Spinner.Position = [250 260 100 22];

            % Create DropDown
            app.DropDown = uidropdown(app.Panel);
            app.DropDown.Placeholder = 'Pick one';
            app.DropDown.Items = {'Ten', 'Twenty', 'Thirty'};
            app.DropDown.ItemsData = [10 20 30];
            app.DropDown.Value = 20;
            app.DropDown.Editable = 'off';
            app.DropDown.Position = [10 230 120 22];

            % Create ListBox
            app.ListBox = uilistbox(app.Panel);
            app.ListBox.Items = {'A', 'B', 'C', 'D'};
            app.ListBox.Multiselect = 'on';
            app.ListBox.Value = {'A', 'C'};
            app.ListBox.Position = [140 160 100 90];

            % Create TextArea
            app.TextArea = uitextarea(app.Panel);
            app.TextArea.Value = {'line 1'; 'line 2'};
            app.TextArea.Editable = 'off';
            app.TextArea.Position = [250 160 150 90];
            app.TextArea.WordWrap = 'off';

            % Create DatePicker
            app.DatePicker = uidatepicker(app.Panel);
            app.DatePicker.Value = datetime([2026 10 18]);
            app.DatePicker.DisabledDates = [datetime([2026 12 25]); datetime([2027 1 1])];
            app.DatePicker.DisplayFormat = 'yyyy-MM-dd';
            app.DatePicker.Position = [10 130 120 22];

            % Create ColorPicker
            app.ColorPicker = uicolorpicker(app.Panel);
            app.ColorPicker.Value = [1 0 0];
            app.ColorPicker.Position = [10 100 50 22];

            % Create Hyperlink
            app.Hyperlink = uihyperlink(app.Panel);
            app.Hyperlink.URL = 'https://example.com';
            app.Hyperlink.Text = 'Example';
            app.Hyperlink.Position = [70 100 100 22];
            app.Hyperlink.VisitedColor = [0.5 0 0.5];

            % Create HTML
            app.HTML = uihtml(app.Panel);
            app.HTML.HTMLSource = '<b>Bold</b> text';
            app.HTML.Position = [180 80 200 40];

            % Create Image
            app.Image = uiimage(app.Panel);
            app.Image.ImageSource = fullfile(pathToMLAPP, 'img', 'logo.png');
            app.Image.ScaleMethod = 'fill';
            app.Image.Position = [400 300 100 100];
            app.Image.Tooltip = 'Logo';

            % Create ButtonGroup
            app.ButtonGroup = uibuttongroup(app.Panel);
            app.ButtonGroup.Title = 'Mode';
            app.ButtonGroup.Position = [400 150 150 120];
            app.ButtonGroup.SelectionChangedFcn = createCallbackFcn(app, @ButtonGroupSelectionChanged, true);
            app.ButtonGroup.BorderType = 'line';

            % Create RadioButton
            app.RadioButton = uiradiobutton(app.ButtonGroup);
            app.RadioButton.Text = 'Fast';
            app.RadioButton.Position = [10 70 60 22];

            % Create RadioButton2
            app.RadioButton2 = uiradiobutton(app.ButtonGroup);
            app.RadioButton2.Position = [10 45 60 22];
            app.RadioButton2.Value = true;
            app.RadioButton2.Text = 'Slow';

            % Create ToggleGroup
            app.ToggleGroup = uibuttongroup(app.Panel);
            app.ToggleGroup.Position = [400 20 150 120];

            % Create ToggleButton
            app.ToggleButton = uitogglebutton(app.ToggleGroup);
            app.ToggleButton.Text = 'Off';
            app.ToggleButton.Position = [10 10 100 23];
            app.ToggleButton.Value = true;

            % Create GaugesTab
            app.GaugesTab = uitab(app.TabGroup);
            app.GaugesTab.Title = 'Gauges';

            % Create Slider
            app.Slider = uislider(app.GaugesTab);
            app.Slider.Limits = [0 50];
            app.Slider.MajorTicks = [0 25 50];
            app.Slider.Value = 25;
            app.Slider.Step = 2;
            app.Slider.Position = [20 380 200 3];

            % Create RangeSlider
            app.RangeSlider = uislider(app.GaugesTab, 'range');
            app.RangeSlider.Value = [10 20];
            app.RangeSlider.Position = [20 320 200 3];
            app.RangeSlider.Orientation = 'horizontal';

            % Create Gauge
            app.Gauge = uigauge(app.GaugesTab, 'circular');
            app.Gauge.ScaleColors = [1 0 0; 0 1 0];
            app.Gauge.ScaleColorLimits = [0 50; 50 100];
            app.Gauge.Value = 42;
            app.Gauge.Position = [250 250 120 120];

            % Create LinearGauge
            app.LinearGauge = uigauge(app.GaugesTab, 'linear');
            app.LinearGauge.Limits = [-10 10];
            app.LinearGauge.Position = [20 200 120 40];

            % Create NinetyDegreeGauge
            app.NinetyDegreeGauge = uigauge(app.GaugesTab, 'ninetydegree');
            app.NinetyDegreeGauge.ScaleDirection = 'counterclockwise';
            app.NinetyDegreeGauge.Position = [150 150 90 90];

            % Create SemicircularGauge
            app.SemicircularGauge = uigauge(app.GaugesTab, 'semicircular');
            app.SemicircularGauge.MinorTicks = [];
            app.SemicircularGauge.Position = [250 150 120 65];

            % Create Knob
            app.Knob = uiknob(app.GaugesTab, 'continuous');
            app.Knob.MajorTicks = [0 50 100];
            app.Knob.Value = 30;
            app.Knob.Position = [400 300 60 60];

            % Create DiscreteKnob
            app.DiscreteKnob = uiknob(app.GaugesTab, 'discrete');
            app.DiscreteKnob.Items = {'Low', 'High'};
            app.DiscreteKnob.Value = 'High';
            app.DiscreteKnob.Position = [400 200 60 60];

            % Create Switch
            app.Switch = uiswitch(app.GaugesTab, 'slider');
            app.Switch.Value = 'On';
            app.Switch.Position = [20 100 45 20];

            % Create RockerSwitch
            app.RockerSwitch = uiswitch(app.GaugesTab, 'rocker');
            app.RockerSwitch.Items = {'Stop', 'Run'};
            app.RockerSwitch.Value = 'Run';
            app.RockerSwitch.Orientation = 'horizontal';
            app.RockerSwitch.Position = [100 100 45 20];

            % Create ToggleSwitch
            app.ToggleSwitch = uiswitch(app.GaugesTab, 'toggle');
            app.ToggleSwitch.Position = [180 90 20 45];

            % Create Lamp
            app.Lamp = uilamp(app.GaugesTab);
            app.Lamp.Color = [0 1 0];
            app.Lamp.Position = [240 100 20 20];

            % Create Tree
            app.Tree = uitree(app.GaugesTab);
            app.Tree.Position = [300 20 150 100];
            app.Tree.Multiselect = 'on';
            app.Tree.SelectionChangedFcn = createCallbackFcn(app, @TreeSelectionChanged, true);

            % Create Node1
            app.Node1 = uitreenode(app.Tree);
            app.Node1.Text = 'First';
            app.Node1.NodeData = 1;

            % Create Node2
            app.Node2 = uitreenode(app.Node1);
            app.Node2.Text = 'Second';

            % Create CheckTree
            app.CheckTree = uitree(app.GaugesTab, 'checkbox');
            app.CheckTree.Position = [460 20 150 100];

            % Create Check1
            app.Check1 = uitreenode(app.CheckTree);
            app.Check1.Text = 'One';

            % Create Check2
            app.Check2 = uitreenode(app.CheckTree);
            app.Check2.Text = 'Two';

            % Create Check3
            app.Check3 = uitreenode(app.Check2);
            app.Check3.Text = 'Three';

            % Assign Checked Nodes
            app.CheckTree.CheckedNodes = [app.Check1, app.Check3, app.Check2];

            % Create ContextMenu
            app.ContextMenu = uicontextmenu(app.UIFigure);

            % Create CopyMenu
            app.CopyMenu = uimenu(app.ContextMenu);
            app.CopyMenu.Text = 'Copy';

            % Assign app.ContextMenu
            app.Table.ContextMenu = app.ContextMenu;

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = DesignerSpecApp

            % Create UIFigure and components
            createComponents(app)

            % Register the app with App Designer
            registerApp(app, app.UIFigure)

            if nargout == 0
                clear app
            end
        end

        % Code that executes before app deletion
        function delete(app)

            % Delete UIFigure when app is deleted
            delete(app.UIFigure)
        end
    end
end
