classdef OptionsDialog < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure        matlab.ui.Figure
        GridLayout      matlab.ui.container.GridLayout
        ColorLabel      matlab.ui.control.Label
        ColorDropDown   matlab.ui.control.DropDown
        WidthLabel      matlab.ui.control.Label
        WidthSpinner    matlab.ui.control.Spinner
        OKButton        matlab.ui.control.Button
        CancelButton    matlab.ui.control.Button
    end


    properties (Access = private)
        CallingApp % the main app, whose public functions take the choices
        Colors = {[0 0.447 0.741], [0.85 0.325 0.098], [0.466 0.674 0.188], [0 0 0]} % in the drop-down's order
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app, mainapp, color, width)
            app.CallingApp = mainapp;
            match = find(cellfun(@(c) isequal(c, color), app.Colors), 1);
            if ~isempty(match)
                app.ColorDropDown.Value = app.ColorDropDown.Items{match};
            end
            app.WidthSpinner.Value = width;
        end

        % Button pushed function: OKButton
        function OKButtonPushed(app, event)
            color = app.Colors{strcmp(app.ColorDropDown.Items, app.ColorDropDown.Value)};
            updatePlot(app.CallingApp, color, app.WidthSpinner.Value);
            UIFigureCloseRequest(app, event);
        end

        % Button pushed function: CancelButton
        function CancelButtonPushed(app, event)
            UIFigureCloseRequest(app, event);
        end

        % Close request function: UIFigure
        function UIFigureCloseRequest(app, event)
            % The main app takes its button back before the dialog goes
            if isvalid(app.CallingApp)
                optionsClosed(app.CallingApp);
            end
            delete(app)
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [700 300 280 140];
            app.UIFigure.Name = 'Options';
            app.UIFigure.Resize = 'off';
            app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.RowHeight = {22, 22, '1x', 22};

            % Create ColorLabel
            app.ColorLabel = uilabel(app.GridLayout);
            app.ColorLabel.HorizontalAlignment = 'right';
            app.ColorLabel.Layout.Row = 1;
            app.ColorLabel.Layout.Column = 1;
            app.ColorLabel.Text = 'Color';

            % Create ColorDropDown
            app.ColorDropDown = uidropdown(app.GridLayout);
            app.ColorDropDown.Items = {'Blue', 'Orange', 'Green', 'Black'};
            app.ColorDropDown.Layout.Row = 1;
            app.ColorDropDown.Layout.Column = 2;
            app.ColorDropDown.Value = 'Blue';

            % Create WidthLabel
            app.WidthLabel = uilabel(app.GridLayout);
            app.WidthLabel.HorizontalAlignment = 'right';
            app.WidthLabel.Layout.Row = 2;
            app.WidthLabel.Layout.Column = 1;
            app.WidthLabel.Text = 'Line width';

            % Create WidthSpinner
            app.WidthSpinner = uispinner(app.GridLayout);
            app.WidthSpinner.Step = 0.5;
            app.WidthSpinner.Limits = [0.5 5];
            app.WidthSpinner.Layout.Row = 2;
            app.WidthSpinner.Layout.Column = 2;
            app.WidthSpinner.Value = 1.5;

            % Create OKButton
            app.OKButton = uibutton(app.GridLayout, 'push');
            app.OKButton.ButtonPushedFcn = createCallbackFcn(app, @OKButtonPushed, true);
            app.OKButton.Layout.Row = 4;
            app.OKButton.Layout.Column = 1;
            app.OKButton.Text = 'OK';

            % Create CancelButton
            app.CancelButton = uibutton(app.GridLayout, 'push');
            app.CancelButton.ButtonPushedFcn = createCallbackFcn(app, @CancelButtonPushed, true);
            app.CancelButton.Layout.Row = 4;
            app.CancelButton.Layout.Column = 2;
            app.CancelButton.Text = 'Cancel';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = OptionsDialog(varargin)

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

            % Delete UIFigure when app is deleted
            delete(app.UIFigure)
        end
    end
end
