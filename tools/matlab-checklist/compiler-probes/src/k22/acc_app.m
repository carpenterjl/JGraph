classdef acc_app < matlab.apps.AppBase
    %ACC_APP The acceptance set's windowed app (compiler and SDK plan, stage K22), in App Designer's
    %shape: a window titled 'acc app' with a label and a Compute button, which puts acc_add and
    %acc_stats's answers in the label, logs them and closes the app. A windowed program's output
    %goes nowhere, so each line is appended to the file K6_LOG names (acc_log).

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        ResultLabel    matlab.ui.control.Label
        ComputeButton  matlab.ui.control.Button
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            beep('off');
            acc_log('started');
            acc_log(sprintf('isdeployed %d', isdeployed));
        end

        % Button pushed function: ComputeButton
        function ComputeButtonPushed(app, event)
            s = acc_stats([3 1 4 1 5]);
            text = sprintf('add=%g n=%d mean=%g max=%g', acc_add(2, 3), s.n, s.mean, s.max);
            app.ResultLabel.Text = text;
            acc_log(['computed ' text]);
            acc_log(['label ' app.ResultLabel.Text]);
            acc_log('closing');
            delete(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 320 140];
            app.UIFigure.Name = 'acc app';

            % Create ResultLabel
            app.ResultLabel = uilabel(app.UIFigure);
            app.ResultLabel.Position = [20 85 280 22];
            app.ResultLabel.Text = 'press Compute';

            % Create ComputeButton
            app.ComputeButton = uibutton(app.UIFigure, 'push');
            app.ComputeButton.ButtonPushedFcn = createCallbackFcn(app, @ComputeButtonPushed, true);
            app.ComputeButton.Position = [110 35 100 22];
            app.ComputeButton.Text = 'Compute';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = acc_app

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
