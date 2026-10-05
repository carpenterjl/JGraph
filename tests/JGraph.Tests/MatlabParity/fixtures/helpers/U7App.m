classdef U7App < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure    matlab.ui.Figure
        GridLayout  matlab.ui.container.GridLayout
        AmpLabel    matlab.ui.control.Label
        AmpField    matlab.ui.control.NumericEditField
        NameLabel   matlab.ui.control.Label
        NameField   matlab.ui.control.EditField
        PlotButton  matlab.ui.control.Button
        UIAxes      matlab.ui.control.UIAxes
    end


    properties (Access = private)
        Presses = 0 % how many times the button was pushed
    end

    properties (Access = public)
        Scale = 1 % what the amplitude is multiplied by
    end

    methods (Access = private)

        function redraw(app)
            x = linspace(0, 1, 11);
            plot(app.UIAxes, x, app.AmpField.Value * app.Scale * x);
            title(app.UIAxes, app.NameField.Value);
        end
    end

    methods (Access = public)

        function n = presses(app)
            n = app.Presses;
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app, amp, name)
            u7_note(sprintf('startup %d', nargin));
            if nargin > 1
                app.AmpField.Value = amp;
            end
            if nargin > 2
                app.NameField.Value = name;
            end
            redraw(app);
        end

        % Button pushed function: PlotButton
        function PlotButtonPushed(app, event)
            app.Presses = app.Presses + 1;
            u7_note(['pushed ' class(event)]);
            redraw(app);
        end

        % Value changed function: AmpField
        function AmpFieldValueChanged(app, event)
            value = app.AmpField.Value;
            u7_note(sprintf('amp %g %s', value, class(event)));
        end

        % Value changed function: NameField
        function NameFieldValueChanged(app)
            u7_note(sprintf('name %s %d', app.NameField.Value, nargin));
        end

        % Close request function: UIFigure
        function UIFigureCloseRequest(app, event)
            u7_note('closereq');
            delete(app)
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 480 360];
            app.UIFigure.Name = 'U7 App';
            app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {80, '1x'};
            app.GridLayout.RowHeight = {22, 22, 22, '1x'};

            % Create AmpLabel
            app.AmpLabel = uilabel(app.GridLayout);
            app.AmpLabel.HorizontalAlignment = 'right';
            app.AmpLabel.Layout.Row = 1;
            app.AmpLabel.Layout.Column = 1;
            app.AmpLabel.Text = 'Amplitude';

            % Create AmpField
            app.AmpField = uieditfield(app.GridLayout, 'numeric');
            app.AmpField.ValueChangedFcn = createCallbackFcn(app, @AmpFieldValueChanged, true);
            app.AmpField.Tag = 'amp';
            app.AmpField.Layout.Row = 1;
            app.AmpField.Layout.Column = 2;
            app.AmpField.Value = 2;

            % Create NameLabel
            app.NameLabel = uilabel(app.GridLayout);
            app.NameLabel.HorizontalAlignment = 'right';
            app.NameLabel.Layout.Row = 2;
            app.NameLabel.Layout.Column = 1;
            app.NameLabel.Text = 'Name';

            % Create NameField
            app.NameField = uieditfield(app.GridLayout, 'text');
            app.NameField.ValueChangedFcn = createCallbackFcn(app, @NameFieldValueChanged, false);
            app.NameField.Tag = 'name';
            app.NameField.Layout.Row = 2;
            app.NameField.Layout.Column = 2;
            app.NameField.Value = 'line';

            % Create PlotButton
            app.PlotButton = uibutton(app.GridLayout, 'push');
            app.PlotButton.ButtonPushedFcn = createCallbackFcn(app, @PlotButtonPushed, true);
            app.PlotButton.Tag = 'plot';
            app.PlotButton.Layout.Row = 3;
            app.PlotButton.Layout.Column = [1 2];
            app.PlotButton.Text = 'Plot';

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Title')
            xlabel(app.UIAxes, 'X')
            ylabel(app.UIAxes, 'Y')
            app.UIAxes.Layout.Row = 4;
            app.UIAxes.Layout.Column = [1 2];

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = U7App(varargin)

            % Create UIFigure and components
            createComponents(app)

            % Register the app with App Designer
            registerApp(app, app.UIFigure)

            % Execute the startup function
            runStartupFcn(app, @(app)startupFcn(app, varargin{:}))

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
