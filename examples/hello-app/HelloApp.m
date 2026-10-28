classdef HelloApp < matlab.apps.AppBase
    %HELLOAPP A Hello World app, in App Designer's shape, that a JGraph standalone Windows
    %application is built from (build_hello_app). It shows the pieces an app is made of: a
    %uifigure with a grid layout, labels, a text field and a Greet button that answers it, a
    %drop-down, a numeric field and a slider that drive a plot in a uiaxes through a function of
    %its own (hello_wave), and a status line. Run it at the prompt with HelloApp, or build it and
    %start HelloApp.exe. A windowed program's output goes nowhere, so with HELLO_LOG set each
    %status line is also appended to that file with the time.

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure         matlab.ui.Figure
        GridLayout       matlab.ui.container.GridLayout
        TitleLabel       matlab.ui.control.Label
        NameLabel        matlab.ui.control.Label
        NameField        matlab.ui.control.EditField
        GreetButton      matlab.ui.control.Button
        GreetingLabel    matlab.ui.control.Label
        ShapeLabel       matlab.ui.control.Label
        ShapeDropDown    matlab.ui.control.DropDown
        FrequencyLabel   matlab.ui.control.Label
        FrequencyField   matlab.ui.control.NumericEditField
        AmplitudeLabel   matlab.ui.control.Label
        AmplitudeSlider  matlab.ui.control.Slider
        PlotButton       matlab.ui.control.Button
        UIAxes           matlab.ui.control.UIAxes
        StatusLabel      matlab.ui.control.Label
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            beep('off');
            note(app, 'started');
            note(app, sprintf('isdeployed %d', isdeployed));
            replot(app);
        end

        % Button pushed function: GreetButton
        function GreetButtonPushed(app, event)
            name = strtrim(app.NameField.Value);
            if isempty(name)
                name = 'World';
            end
            app.GreetingLabel.Text = sprintf('Hello, %s!', name);
            note(app, ['greeting ' app.GreetingLabel.Text]);
        end

        % Button pushed function: PlotButton
        function PlotButtonPushed(app, event)
            replot(app);
        end

        % Value changed function: ShapeDropDown, FrequencyField, AmplitudeSlider
        function ControlValueChanged(app, event)
            replot(app);
        end
    end

    % What the app does
    methods (Access = private)

        % Draws the wave the controls describe
        function replot(app)
            shape = app.ShapeDropDown.Value;
            frequency = app.FrequencyField.Value;
            amplitude = app.AmplitudeSlider.Value;
            [x, y] = hello_wave(shape, frequency, amplitude);
            plot(app.UIAxes, x, y, 'LineWidth', 1.5);
            title(app.UIAxes, sprintf('%s wave, %g Hz', shape, frequency));
            xlabel(app.UIAxes, 'Time (s)');
            ylabel(app.UIAxes, 'Amplitude');
            grid(app.UIAxes, 'on');
            app.UIAxes.YLim = [-2.2 2.2];
            note(app, sprintf('plot %s f=%g a=%g n=%d', shape, frequency, amplitude, numel(y)));
        end

        % Puts a line in the status label and, with HELLO_LOG set, in that file. The label is
        % gone by the time delete runs for a window the user closed, so the file comes first.
        function note(app, line)
            log = getenv('HELLO_LOG');
            if ~isempty(log)
                fid = fopen(log, 'a');
                if fid > 0
                    fprintf(fid, '%s\t%s\n', char(datetime('now', 'Format', 'HH:mm:ss.SSS')), line);
                    fclose(fid);
                end
            end
            if isvalid(app.StatusLabel)
                app.StatusLabel.Text = line;
            end
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 560 480];
            app.UIFigure.Name = 'Hello World';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {'fit', '1x', 'fit', '1x'};
            app.GridLayout.RowHeight = {'fit', 'fit', 'fit', 'fit', '1x', 'fit'};

            % Create TitleLabel
            app.TitleLabel = uilabel(app.GridLayout);
            app.TitleLabel.HorizontalAlignment = 'center';
            app.TitleLabel.FontSize = 20;
            app.TitleLabel.FontWeight = 'bold';
            app.TitleLabel.Tag = 'TitleLabel';
            app.TitleLabel.Layout.Row = 1;
            app.TitleLabel.Layout.Column = [1 4];
            app.TitleLabel.Text = 'Hello, World!';

            % Create NameLabel
            app.NameLabel = uilabel(app.GridLayout);
            app.NameLabel.HorizontalAlignment = 'right';
            app.NameLabel.Layout.Row = 2;
            app.NameLabel.Layout.Column = 1;
            app.NameLabel.Text = 'Name';

            % Create NameField
            app.NameField = uieditfield(app.GridLayout, 'text');
            app.NameField.Tag = 'NameField';
            app.NameField.Layout.Row = 2;
            app.NameField.Layout.Column = 2;

            % Create GreetButton
            app.GreetButton = uibutton(app.GridLayout, 'push');
            app.GreetButton.ButtonPushedFcn = createCallbackFcn(app, @GreetButtonPushed, true);
            app.GreetButton.Tag = 'GreetButton';
            app.GreetButton.Layout.Row = 2;
            app.GreetButton.Layout.Column = 3;
            app.GreetButton.Text = 'Greet';

            % Create GreetingLabel
            app.GreetingLabel = uilabel(app.GridLayout);
            app.GreetingLabel.FontWeight = 'bold';
            app.GreetingLabel.Tag = 'GreetingLabel';
            app.GreetingLabel.Layout.Row = 2;
            app.GreetingLabel.Layout.Column = 4;
            app.GreetingLabel.Text = 'press Greet';

            % Create ShapeLabel
            app.ShapeLabel = uilabel(app.GridLayout);
            app.ShapeLabel.HorizontalAlignment = 'right';
            app.ShapeLabel.Layout.Row = 3;
            app.ShapeLabel.Layout.Column = 1;
            app.ShapeLabel.Text = 'Shape';

            % Create ShapeDropDown
            app.ShapeDropDown = uidropdown(app.GridLayout);
            app.ShapeDropDown.Items = {'sine', 'square', 'damped'};
            app.ShapeDropDown.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.ShapeDropDown.Tag = 'ShapeDropDown';
            app.ShapeDropDown.Layout.Row = 3;
            app.ShapeDropDown.Layout.Column = 2;
            app.ShapeDropDown.Value = 'sine';

            % Create FrequencyLabel
            app.FrequencyLabel = uilabel(app.GridLayout);
            app.FrequencyLabel.HorizontalAlignment = 'right';
            app.FrequencyLabel.Layout.Row = 3;
            app.FrequencyLabel.Layout.Column = 3;
            app.FrequencyLabel.Text = 'Frequency (Hz)';

            % Create FrequencyField
            app.FrequencyField = uieditfield(app.GridLayout, 'numeric');
            app.FrequencyField.Limits = [0.1 10];
            app.FrequencyField.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.FrequencyField.Tag = 'FrequencyField';
            app.FrequencyField.Layout.Row = 3;
            app.FrequencyField.Layout.Column = 4;
            app.FrequencyField.Value = 1;

            % Create AmplitudeLabel
            app.AmplitudeLabel = uilabel(app.GridLayout);
            app.AmplitudeLabel.HorizontalAlignment = 'right';
            app.AmplitudeLabel.Layout.Row = 4;
            app.AmplitudeLabel.Layout.Column = 1;
            app.AmplitudeLabel.Text = 'Amplitude';

            % Create AmplitudeSlider
            app.AmplitudeSlider = uislider(app.GridLayout);
            app.AmplitudeSlider.Limits = [0 2];
            app.AmplitudeSlider.ValueChangedFcn = createCallbackFcn(app, @ControlValueChanged, true);
            app.AmplitudeSlider.Tag = 'AmplitudeSlider';
            app.AmplitudeSlider.Layout.Row = 4;
            app.AmplitudeSlider.Layout.Column = [2 3];
            app.AmplitudeSlider.Value = 1;

            % Create PlotButton
            app.PlotButton = uibutton(app.GridLayout, 'push');
            app.PlotButton.ButtonPushedFcn = createCallbackFcn(app, @PlotButtonPushed, true);
            app.PlotButton.Tag = 'PlotButton';
            app.PlotButton.Layout.Row = 4;
            app.PlotButton.Layout.Column = 4;
            app.PlotButton.Text = 'Plot';

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Wave')
            xlabel(app.UIAxes, 'Time (s)')
            ylabel(app.UIAxes, 'Amplitude')
            app.UIAxes.Tag = 'UIAxes';
            app.UIAxes.Layout.Row = 5;
            app.UIAxes.Layout.Column = [1 4];

            % Create StatusLabel
            app.StatusLabel = uilabel(app.GridLayout);
            app.StatusLabel.FontColor = [0.35 0.35 0.35];
            app.StatusLabel.Tag = 'StatusLabel';
            app.StatusLabel.Layout.Row = 6;
            app.StatusLabel.Layout.Column = [1 4];
            app.StatusLabel.Text = '';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = HelloApp

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
            note(app, 'closing');

            % Delete UIFigure when app is deleted
            delete(app.UIFigure)
        end
    end
end
