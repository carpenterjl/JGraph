classdef U7Single < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure  matlab.ui.Figure
        Label     matlab.ui.control.Label
    end

    properties (Access = public)
        Started = 0 % the argument the app was started with
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app, n)
            u7_note(sprintf('startup %g', n));
            app.Started = n;
            app.Label.Text = sprintf('%g', n);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 200 80];
            app.UIFigure.Name = 'U7 Single';

            % Create Label
            app.Label = uilabel(app.UIFigure);
            app.Label.Position = [20 20 100 22];

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = U7Single(varargin)

            runningApp = getRunningApp(app);

            % Check for running singleton app
            if isempty(runningApp)

                % Create UIFigure and components
                createComponents(app)

                % Register the app with App Designer
                registerApp(app, app.UIFigure)

                % Execute the startup function
                runStartupFcn(app, @(app)startupFcn(app, varargin{:}))
            else

                % Focus the running singleton app
                figure(runningApp.UIFigure)

                app = runningApp;
            end

            if nargout == 0
                clear app
            end
        end

        % Code that executes before app deletion
        function delete(app)
            u7_note(sprintf('delete %d', isempty(app.UIFigure)));

            % Delete UIFigure when app is deleted
            delete(app.UIFigure)
        end
    end
end
