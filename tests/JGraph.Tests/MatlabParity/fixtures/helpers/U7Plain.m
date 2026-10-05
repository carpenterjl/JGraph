classdef U7Plain < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure  matlab.ui.Figure
        Button    matlab.ui.control.Button
        Panel     matlab.ui.container.Panel
    end

    properties (Access = public)
        Mode = 'ok' % what the startup function is to do
        Seen = '' % what the startup function saw
    end

    methods (Access = public)

        % The handles a class makes for itself, handed out so that a script can try them.
        function h = wrap(app, wantsEvent)
            h = createCallbackFcn(app, @noted, wantsEvent);
        end

        function h = wrapAnonymous(app)
            h = createCallbackFcn(app, @(a, e) u7_note(['anonymous ' class(a) ' ' class(e)]), true);
        end

        function h = wrapPublic(app)
            h = createCallbackFcn(app, @poke, false);
        end

        function poke(app)
            u7_note('poke');
        end

        function r = running(app)
            r = getRunningApp(app);
        end

        function resize(app, value)
            setAutoResize(app, app.UIFigure, value);
        end

        function again(app)
            registerApp(app, app.UIFigure);
        end

        function registerOther(app, other)
            registerApp(app, other);
        end

        function start(app, fn)
            runStartupFcn(app, fn);
        end
    end

    % Callbacks that handle component events
    methods (Access = private)

        function noted(app, varargin)
            u7_note(sprintf('noted %d', nargin));
        end

        % Code that executes after component creation
        function startupFcn(app)
            u7_note('startup');
            fig = get(groot, 'CurrentFigure');
            app.Seen = sprintf('%s %d %d', app.UIFigure.HandleVisibility, isempty(fig), ...
                numel(findobj('Type', 'figure', 'Name', 'U7 Plain')));
            if strcmp(app.Mode, 'fail')
                error('U7:startup', 'The startup function failed.');
            end
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 240 120];
            app.UIFigure.Name = 'U7 Plain';
            if strcmp(app.Mode, 'callback')
                app.UIFigure.HandleVisibility = 'callback';
            end

            % Create Panel
            app.Panel = uipanel(app.UIFigure);
            app.Panel.Position = [10 10 220 100];

            % Create Button
            app.Button = uibutton(app.Panel, 'push');
            app.Button.Position = [10 10 100 22];

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = U7Plain(mode)
            if nargin > 0
                app.Mode = mode;
            end

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
            u7_note('delete');

            % Delete UIFigure when app is deleted
            delete(app.UIFigure)
        end
    end
end
