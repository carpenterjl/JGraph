classdef PolarApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        GridLayout     matlab.ui.container.GridLayout
        PlotPanel      matlab.ui.container.Panel
        ControlsGrid   matlab.ui.container.GridLayout
        LobesLabel     matlab.ui.control.Label
        LobesSpinner   matlab.ui.control.Spinner
        ZeroLabel      matlab.ui.control.Label
        ZeroDropDown   matlab.ui.control.DropDown
    end


    properties (Access = private)
        PolarAxes % the polar axes, made by the startup function: the library has none
    end

    methods (Access = private)

        % Draws a curve with as many lobes as the spinner says
        function draw(app)
            theta = linspace(0, 2 * pi, 721);
            rho = 1 + 0.5 * cos(app.LobesSpinner.Value * theta);
            polarplot(app.PolarAxes, theta, rho, 'LineWidth', 1.5);
            app.PolarAxes.RLim = [0 1.6];
            app.PolarAxes.ThetaZeroLocation = app.ZeroDropDown.Value;
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            app.PolarAxes = polaraxes(app.PlotPanel);
            draw(app);
        end

        % Value changed function: LobesSpinner, ZeroDropDown
        function ControlValueChanged(app, event)
            draw(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 480 520];
            app.UIFigure.Name = 'Polar Plot';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {'1x'};
            app.GridLayout.RowHeight = {'1x', 32};

            % Create PlotPanel
            app.PlotPanel = uipanel(app.GridLayout);
            app.PlotPanel.BorderType = 'none';
            app.PlotPanel.Layout.Row = 1;
            app.PlotPanel.Layout.Column = 1;

            % Create ControlsGrid
            app.ControlsGrid = uigridlayout(app.GridLayout);
            app.ControlsGrid.ColumnWidth = {60, 80, 100, 100};
            app.ControlsGrid.RowHeight = {22};
            app.ControlsGrid.Padding = [0 5 0 5];
            app.ControlsGrid.Layout.Row = 2;
            app.ControlsGrid.Layout.Column = 1;

            % Create LobesLabel
            app.LobesLabel = uilabel(app.ControlsGrid);
            app.LobesLabel.HorizontalAlignment = 'right';
            app.LobesLabel.Layout.Row = 1;
            app.LobesLabel.Layout.Column = 1;
            app.LobesLabel.Text = 'Lobes';

            % Create LobesSpinner
            app.LobesSpinner = uispinner(app.ControlsGrid);
            app.LobesSpinner.Limits = [1 12];
            app.LobesSpinner.RoundFractionalValues = 'on';
            app.LobesSpinner.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.LobesSpinner.Layout.Row = 1;
            app.LobesSpinner.Layout.Column = 2;
            app.LobesSpinner.Value = 4;

            % Create ZeroLabel
            app.ZeroLabel = uilabel(app.ControlsGrid);
            app.ZeroLabel.HorizontalAlignment = 'right';
            app.ZeroLabel.Layout.Row = 1;
            app.ZeroLabel.Layout.Column = 3;
            app.ZeroLabel.Text = 'Zero angle';

            % Create ZeroDropDown
            app.ZeroDropDown = uidropdown(app.ControlsGrid);
            app.ZeroDropDown.Items = {'right', 'top', 'left', 'bottom'};
            app.ZeroDropDown.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.ZeroDropDown.Layout.Row = 1;
            app.ZeroDropDown.Layout.Column = 4;
            app.ZeroDropDown.Value = 'right';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = PolarApp

            % Create UIFigure and components
            createComponents(app)

            % Register the app with App Designer
            registerApp(app, app.UIFigure)

            % Execute the startup function
            runStartupFcn(app, @startupFcn)

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
