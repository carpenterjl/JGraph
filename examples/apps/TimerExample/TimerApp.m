classdef TimerApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        GridLayout     matlab.ui.container.GridLayout
        StartButton    matlab.ui.control.Button
        StopButton     matlab.ui.control.Button
        PeriodLabel    matlab.ui.control.Label
        PeriodSpinner  matlab.ui.control.Spinner
        CountLabel     matlab.ui.control.Label
        UIAxes         matlab.ui.control.UIAxes
    end


    properties (Access = private)
        Timer % the timer that takes a sample each period
        Samples = zeros(1, 0) % the samples taken, oldest first
    end

    methods (Access = private)

        % Takes a sample - a slow wave with some noise on it - and plots
        % the last 100
        function tick(app)
            n = numel(app.Samples);
            app.Samples(n + 1) = sin(n / 8) + 0.2 * randn;
            shown = app.Samples(max(1, end - 99):end);
            plot(app.UIAxes, (numel(app.Samples) - numel(shown) + 1):numel(app.Samples), shown, 'LineWidth', 1.5);
            app.CountLabel.Text = sprintf('%d samples', numel(app.Samples));
        end

        % Shows which of Start and Stop can be pushed
        function showRunning(app)
            if strcmp(app.Timer.Running, 'on')
                app.StartButton.Enable = 'off';
                app.StopButton.Enable = 'on';
            else
                app.StartButton.Enable = 'on';
                app.StopButton.Enable = 'off';
            end
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            app.Timer = timer('ExecutionMode', 'fixedRate', 'BusyMode', 'drop', ...
                'Period', app.PeriodSpinner.Value, 'TimerFcn', @(~, ~) tick(app));
            showRunning(app);
        end

        % Button pushed function: StartButton
        function StartButtonPushed(app, event)
            start(app.Timer);
            showRunning(app);
        end

        % Button pushed function: StopButton
        function StopButtonPushed(app, event)
            stop(app.Timer);
            showRunning(app);
        end

        % Value changed function: PeriodSpinner
        function PeriodSpinnerValueChanged(app, event)
            % A running timer's period cannot change: stop it first
            running = strcmp(app.Timer.Running, 'on');
            if running
                stop(app.Timer);
            end
            app.Timer.Period = app.PeriodSpinner.Value;
            if running
                start(app.Timer);
            end
        end

        % Close request function: UIFigure
        function UIFigureCloseRequest(app, event)
            stop(app.Timer);
            delete(app.Timer);
            delete(app)
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 640 420];
            app.UIFigure.Name = 'Timer';
            app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {100, 100, 80, 80, '1x'};
            app.GridLayout.RowHeight = {22, '1x'};

            % Create StartButton
            app.StartButton = uibutton(app.GridLayout, 'push');
            app.StartButton.ButtonPushedFcn = createCallbackFcn(app, @StartButtonPushed, true);
            app.StartButton.Layout.Row = 1;
            app.StartButton.Layout.Column = 1;
            app.StartButton.Text = 'Start';

            % Create StopButton
            app.StopButton = uibutton(app.GridLayout, 'push');
            app.StopButton.ButtonPushedFcn = createCallbackFcn(app, @StopButtonPushed, true);
            app.StopButton.Layout.Row = 1;
            app.StopButton.Layout.Column = 2;
            app.StopButton.Text = 'Stop';

            % Create PeriodLabel
            app.PeriodLabel = uilabel(app.GridLayout);
            app.PeriodLabel.HorizontalAlignment = 'right';
            app.PeriodLabel.Layout.Row = 1;
            app.PeriodLabel.Layout.Column = 3;
            app.PeriodLabel.Text = 'Period (s)';

            % Create PeriodSpinner
            app.PeriodSpinner = uispinner(app.GridLayout);
            app.PeriodSpinner.Step = 0.1;
            app.PeriodSpinner.Limits = [0.1 5];
            app.PeriodSpinner.ValueChangedFcn = createCallbackFcn(app, @PeriodSpinnerValueChanged, true);
            app.PeriodSpinner.Layout.Row = 1;
            app.PeriodSpinner.Layout.Column = 4;
            app.PeriodSpinner.Value = 0.5;

            % Create CountLabel
            app.CountLabel = uilabel(app.GridLayout);
            app.CountLabel.Layout.Row = 1;
            app.CountLabel.Layout.Column = 5;
            app.CountLabel.Text = '0 samples';

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Samples')
            xlabel(app.UIAxes, 'Sample')
            ylabel(app.UIAxes, 'Value')
            app.UIAxes.XGrid = 'on';
            app.UIAxes.YGrid = 'on';
            app.UIAxes.Layout.Row = 2;
            app.UIAxes.Layout.Column = [1 5];

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = TimerApp

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
