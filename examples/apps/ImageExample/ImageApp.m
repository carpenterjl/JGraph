classdef ImageApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure         matlab.ui.Figure
        GridLayout       matlab.ui.container.GridLayout
        ImageAxes        matlab.ui.control.UIAxes
        HistogramAxes    matlab.ui.control.UIAxes
        LoadButton       matlab.ui.control.Button
        ChannelDropDown  matlab.ui.control.DropDown
    end


    properties (Access = private)
        Picture % the image shown, an m-by-n-by-3 array of uint8
    end

    methods (Access = private)

        % Shows the image and the histogram of the chosen channel
        function show(app)
            imshow(app.Picture, 'Parent', app.ImageAxes);
            switch app.ChannelDropDown.Value
                case 'Red'
                    values = app.Picture(:, :, 1);
                    colour = [0.85 0.2 0.2];
                case 'Green'
                    values = app.Picture(:, :, 2);
                    colour = [0.2 0.65 0.2];
                case 'Blue'
                    values = app.Picture(:, :, 3);
                    colour = [0.2 0.35 0.85];
                otherwise
                    values = mean(double(app.Picture), 3);
                    colour = [0.4 0.4 0.4];
            end
            histogram(app.HistogramAxes, double(values(:)), 'BinEdges', 0:8:256, 'FaceColor', colour);
            app.HistogramAxes.XLim = [0 256];
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            % An image made here, so the app needs no file: a red and a
            % green ramp, and a blue spot in the middle
            [x, y] = meshgrid(linspace(-1, 1, 200));
            app.Picture = uint8(255 * cat(3, (x + 1) / 2, (y + 1) / 2, exp(-4 * (x .^ 2 + y .^ 2))));
            show(app);
        end

        % Button pushed function: LoadButton
        function LoadButtonPushed(app, event)
            [file, folder] = uigetfile({'*.png;*.jpg;*.jpeg;*.bmp;*.tif', 'Images'});
            figure(app.UIFigure);
            if isequal(file, 0)
                return;
            end
            picture = imread(fullfile(folder, file));
            if size(picture, 3) == 1
                picture = repmat(picture, 1, 1, 3);
            end
            if isa(picture, 'uint16')
                picture = uint8(double(picture) / 257);
            elseif ~isa(picture, 'uint8')
                picture = uint8(255 * double(picture));
            end
            app.Picture = picture;
            show(app);
        end

        % Value changed function: ChannelDropDown
        function ChannelDropDownValueChanged(app, event)
            show(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 760 420];
            app.UIFigure.Name = 'Image Analysis';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.RowHeight = {'1x', 22};

            % Create ImageAxes
            app.ImageAxes = uiaxes(app.GridLayout);
            app.ImageAxes.XTick = [];
            app.ImageAxes.YTick = [];
            app.ImageAxes.Layout.Row = 1;
            app.ImageAxes.Layout.Column = 1;

            % Create HistogramAxes
            app.HistogramAxes = uiaxes(app.GridLayout);
            title(app.HistogramAxes, 'Histogram')
            xlabel(app.HistogramAxes, 'Intensity')
            ylabel(app.HistogramAxes, 'Pixels')
            app.HistogramAxes.Layout.Row = 1;
            app.HistogramAxes.Layout.Column = 2;

            % Create LoadButton
            app.LoadButton = uibutton(app.GridLayout, 'push');
            app.LoadButton.ButtonPushedFcn = createCallbackFcn(app, @LoadButtonPushed, true);
            app.LoadButton.Layout.Row = 2;
            app.LoadButton.Layout.Column = 1;
            app.LoadButton.Text = 'Load Image...';

            % Create ChannelDropDown
            app.ChannelDropDown = uidropdown(app.GridLayout);
            app.ChannelDropDown.Items = {'Red', 'Green', 'Blue', 'Gray'};
            app.ChannelDropDown.ValueChangedFcn = createCallbackFcn(app, @ChannelDropDownValueChanged, true);
            app.ChannelDropDown.Layout.Row = 2;
            app.ChannelDropDown.Layout.Column = 2;
            app.ChannelDropDown.Value = 'Red';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = ImageApp

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
