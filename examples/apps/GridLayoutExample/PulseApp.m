classdef PulseApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        GridLayout     matlab.ui.container.GridLayout
        ControlsGrid   matlab.ui.container.GridLayout
        ShapeLabel     matlab.ui.control.Label
        ShapeDropDown  matlab.ui.control.DropDown
        WidthLabel     matlab.ui.control.Label
        WidthSpinner   matlab.ui.control.Spinner
        PeriodLabel    matlab.ui.control.Label
        PeriodSpinner  matlab.ui.control.Spinner
        CountLabel     matlab.ui.control.Label
        CountSpinner   matlab.ui.control.Spinner
        UIAxes         matlab.ui.control.UIAxes
    end


    methods (Access = private)

        % Draws the pulse train the controls describe, one sample every
        % 0.1 ms
        function draw(app)
            period = app.PeriodSpinner.Value;
            width = min(app.WidthSpinner.Value, period);
            t = 0:0.1:(app.CountSpinner.Value * period);
            phase = mod(t, period);
            switch app.ShapeDropDown.Value
                case 'Rectangle'
                    y = double(phase < width);
                case 'Triangle'
                    y = max(0, 1 - abs(phase - width / 2) / (width / 2));
                otherwise
                    y = exp(-((phase - width / 2) / (width / 4)) .^ 2);
            end
            plot(app.UIAxes, t, y, 'LineWidth', 1.5);
            app.UIAxes.YLim = [-0.1 1.1];
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            draw(app);
        end

        % Value changed function: CountSpinner, PeriodSpinner, 
        % ...and 2 other components
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
            app.UIFigure.Position = [100 100 720 400];
            app.UIFigure.Name = 'Pulse Generator';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {220, '1x'};
            app.GridLayout.RowHeight = {'1x'};

            % Create ControlsGrid
            app.ControlsGrid = uigridlayout(app.GridLayout);
            app.ControlsGrid.ColumnWidth = {'1x', 90};
            app.ControlsGrid.RowHeight = {22, 22, 22, 22, '1x'};
            app.ControlsGrid.Layout.Row = 1;
            app.ControlsGrid.Layout.Column = 1;

            % Create ShapeLabel
            app.ShapeLabel = uilabel(app.ControlsGrid);
            app.ShapeLabel.HorizontalAlignment = 'right';
            app.ShapeLabel.Layout.Row = 1;
            app.ShapeLabel.Layout.Column = 1;
            app.ShapeLabel.Text = 'Shape';

            % Create ShapeDropDown
            app.ShapeDropDown = uidropdown(app.ControlsGrid);
            app.ShapeDropDown.Items = {'Rectangle', 'Triangle', 'Gaussian'};
            app.ShapeDropDown.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.ShapeDropDown.Layout.Row = 1;
            app.ShapeDropDown.Layout.Column = 2;
            app.ShapeDropDown.Value = 'Rectangle';

            % Create WidthLabel
            app.WidthLabel = uilabel(app.ControlsGrid);
            app.WidthLabel.HorizontalAlignment = 'right';
            app.WidthLabel.Layout.Row = 2;
            app.WidthLabel.Layout.Column = 1;
            app.WidthLabel.Text = 'Width (ms)';

            % Create WidthSpinner
            app.WidthSpinner = uispinner(app.ControlsGrid);
            app.WidthSpinner.Limits = [1 50];
            app.WidthSpinner.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.WidthSpinner.Layout.Row = 2;
            app.WidthSpinner.Layout.Column = 2;
            app.WidthSpinner.Value = 10;

            % Create PeriodLabel
            app.PeriodLabel = uilabel(app.ControlsGrid);
            app.PeriodLabel.HorizontalAlignment = 'right';
            app.PeriodLabel.Layout.Row = 3;
            app.PeriodLabel.Layout.Column = 1;
            app.PeriodLabel.Text = 'Period (ms)';

            % Create PeriodSpinner
            app.PeriodSpinner = uispinner(app.ControlsGrid);
            app.PeriodSpinner.Step = 5;
            app.PeriodSpinner.Limits = [10 200];
            app.PeriodSpinner.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.PeriodSpinner.Layout.Row = 3;
            app.PeriodSpinner.Layout.Column = 2;
            app.PeriodSpinner.Value = 50;

            % Create CountLabel
            app.CountLabel = uilabel(app.ControlsGrid);
            app.CountLabel.HorizontalAlignment = 'right';
            app.CountLabel.Layout.Row = 4;
            app.CountLabel.Layout.Column = 1;
            app.CountLabel.Text = 'Pulses';

            % Create CountSpinner
            app.CountSpinner = uispinner(app.ControlsGrid);
            app.CountSpinner.Limits = [1 10];
            app.CountSpinner.RoundFractionalValues = 'on';
            app.CountSpinner.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.CountSpinner.Layout.Row = 4;
            app.CountSpinner.Layout.Column = 2;
            app.CountSpinner.Value = 3;

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Pulse train')
            xlabel(app.UIAxes, 'Time (ms)')
            ylabel(app.UIAxes, 'Amplitude')
            app.UIAxes.XGrid = 'on';
            app.UIAxes.YGrid = 'on';
            app.UIAxes.Layout.Row = 1;
            app.UIAxes.Layout.Column = 2;

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = PulseApp

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
