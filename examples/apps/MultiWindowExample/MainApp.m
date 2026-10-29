classdef MainApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        GridLayout     matlab.ui.container.GridLayout
        UIAxes         matlab.ui.control.UIAxes
        OptionsButton  matlab.ui.control.Button
    end


    properties (Access = private)
        Dialog % the options dialog while it is open
        LineColor = [0 0.447 0.741] % the plot's colour
        LineWidth = 1.5 % the plot's line width
    end

    methods (Access = private)

        % Plots the curve in the chosen colour and width
        function draw(app)
            x = linspace(0, 4 * pi, 400);
            plot(app.UIAxes, x, sin(x) .* exp(-x / 8), 'Color', app.LineColor, 'LineWidth', app.LineWidth);
        end
    end

    methods (Access = public)

        % Called by the options dialog when its OK is pushed
        function updatePlot(app, color, width)
            app.LineColor = color;
            app.LineWidth = width;
            draw(app);
        end

        % Called by the options dialog as it closes
        function optionsClosed(app)
            app.Dialog = [];
            app.OptionsButton.Enable = 'on';
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            draw(app);
        end

        % Button pushed function: OptionsButton
        function OptionsButtonPushed(app, event)
            % One dialog at a time: the button waits until it closes
            app.OptionsButton.Enable = 'off';
            app.Dialog = OptionsDialog(app, app.LineColor, app.LineWidth);
        end

        % Close request function: UIFigure
        function UIFigureCloseRequest(app, event)
            delete(app.Dialog);
            delete(app)
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 560 420];
            app.UIFigure.Name = 'Main App';
            app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {'1x', 120};
            app.GridLayout.RowHeight = {'1x', 22};

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Damped sine')
            app.UIAxes.XGrid = 'on';
            app.UIAxes.YGrid = 'on';
            app.UIAxes.Layout.Row = 1;
            app.UIAxes.Layout.Column = [1 2];

            % Create OptionsButton
            app.OptionsButton = uibutton(app.GridLayout, 'push');
            app.OptionsButton.ButtonPushedFcn = createCallbackFcn(app, @OptionsButtonPushed, true);
            app.OptionsButton.Layout.Row = 2;
            app.OptionsButton.Layout.Column = 2;
            app.OptionsButton.Text = 'Options...';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = MainApp

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
