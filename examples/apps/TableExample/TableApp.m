classdef TableApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure    matlab.ui.Figure
        GridLayout  matlab.ui.container.GridLayout
        UITable     matlab.ui.control.Table
        UIAxes      matlab.ui.control.UIAxes
        AddButton   matlab.ui.control.Button
        TotalLabel  matlab.ui.control.Label
    end


    methods (Access = private)

        % Plots what each row costs and says what they come to
        function update(app)
            data = app.UITable.Data;
            cost = data.Price .* data.Quantity;
            bar(app.UIAxes, cost);
            app.UIAxes.XTick = 1:height(data);
            app.UIAxes.XTickLabel = data.Item;
            app.TotalLabel.Text = sprintf('Total: %.2f', sum(cost));
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            app.UITable.Data = table({'Apples'; 'Bread'; 'Cheese'}, [0.5; 2.25; 4.8], [6; 1; 2], ...
                'VariableNames', {'Item', 'Price', 'Quantity'});
            update(app);
        end

        % Cell edit callback: UITable
        function UITableCellEdit(app, event)
            % A price or a quantity below zero is put back as it was
            if isnumeric(event.NewData) && event.NewData < 0
                app.UITable.Data{event.Indices(1), event.Indices(2)} = event.PreviousData;
            end
            update(app);
        end

        % Button pushed function: AddButton
        function AddButtonPushed(app, event)
            data = app.UITable.Data;
            row = table({sprintf('Item %d', height(data) + 1)}, 1, 1, 'VariableNames', data.Properties.VariableNames);
            app.UITable.Data = [data; row];
            update(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 720 400];
            app.UIFigure.Name = 'Shopping List';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.RowHeight = {'1x', 22};

            % Create UITable
            app.UITable = uitable(app.GridLayout);
            app.UITable.ColumnName = {'Item'; 'Price'; 'Quantity'};
            app.UITable.RowName = {};
            app.UITable.ColumnEditable = [true true true];
            app.UITable.CellEditCallback = createCallbackFcn(app, @UITableCellEdit, true);
            app.UITable.Layout.Row = 1;
            app.UITable.Layout.Column = 1;

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Cost')
            ylabel(app.UIAxes, 'Cost')
            app.UIAxes.YGrid = 'on';
            app.UIAxes.Layout.Row = 1;
            app.UIAxes.Layout.Column = 2;

            % Create AddButton
            app.AddButton = uibutton(app.GridLayout, 'push');
            app.AddButton.ButtonPushedFcn = createCallbackFcn(app, @AddButtonPushed, true);
            app.AddButton.Layout.Row = 2;
            app.AddButton.Layout.Column = 1;
            app.AddButton.Text = 'Add Row';

            % Create TotalLabel
            app.TotalLabel = uilabel(app.GridLayout);
            app.TotalLabel.Layout.Row = 2;
            app.TotalLabel.Layout.Column = 2;
            app.TotalLabel.Text = 'Total:';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = TableApp

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
