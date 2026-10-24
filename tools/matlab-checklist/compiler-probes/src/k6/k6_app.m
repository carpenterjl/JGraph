classdef k6_app < matlab.apps.AppBase
    %K6_APP Stage K6's app entry point, in App Designer's shape: a window titled 'k6 app' with one
    %button that closes it. Built as a standalone program from this class text and from the .mlapp
    %probe_k6_build makes of it.

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure     matlab.ui.Figure
        CloseButton  matlab.ui.control.Button
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            beep('off');
            fprintf('k6_app\tstarted\n');
        end

        % Button pushed function: CloseButton
        function CloseButtonPushed(app, event)
            fprintf('k6_app\tclosing\n');
            delete(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 260 120];
            app.UIFigure.Name = 'k6 app';

            % Create CloseButton
            app.CloseButton = uibutton(app.UIFigure, 'push');
            app.CloseButton.ButtonPushedFcn = createCallbackFcn(app, @CloseButtonPushed, true);
            app.CloseButton.Position = [80 45 100 22];
            app.CloseButton.Text = 'Close';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = k6_app

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
