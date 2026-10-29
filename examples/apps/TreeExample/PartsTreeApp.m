classdef PartsTreeApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure       matlab.ui.Figure
        GridLayout     matlab.ui.container.GridLayout
        Tree           matlab.ui.container.Tree
        DetailsPanel   matlab.ui.container.Panel
        DetailsGrid    matlab.ui.container.GridLayout
        NameLabel      matlab.ui.control.Label
        NameValue      matlab.ui.control.Label
        CategoryLabel  matlab.ui.control.Label
        CategoryValue  matlab.ui.control.Label
        QuantityLabel  matlab.ui.control.Label
        QuantityField  matlab.ui.control.NumericEditField
        HintLabel      matlab.ui.control.Label
    end


    methods (Access = private)

        % Shows the part a node holds in the details panel; a category
        % node, or no node, shows none
        function showPart(app, node)
            if isempty(node) || isempty(node.NodeData)
                app.NameValue.Text = '';
                app.CategoryValue.Text = '';
                app.QuantityField.Value = 0;
                app.QuantityField.Enable = 'off';
                return;
            end
            part = node.NodeData;
            app.NameValue.Text = part.Name;
            app.CategoryValue.Text = part.Category;
            app.QuantityField.Value = part.Quantity;
            app.QuantityField.Enable = 'on';
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            % The parts, a node for each under a node for its category
            names = {'Bolt M4', 'Bolt M6', 'Washer M4', 'Bearing 608', 'Bearing 6001'};
            categories = {'Bolts', 'Bolts', 'Washers', 'Bearings', 'Bearings'};
            quantities = [120 80 300 16 12];
            groups = unique(categories, 'stable');
            for g = 1:numel(groups)
                parent = uitreenode(app.Tree, 'Text', groups{g});
                for k = find(strcmp(categories, groups{g}))
                    part = struct('Name', names{k}, 'Category', categories{k}, 'Quantity', quantities(k));
                    uitreenode(parent, 'Text', names{k}, 'NodeData', part);
                end
                expand(parent);
            end
            showPart(app, []);
        end

        % Selection changed function: Tree
        function TreeSelectionChanged(app, event)
            showPart(app, event.SelectedNodes);
        end

        % Node text changed function: Tree
        function TreeNodeTextChanged(app, event)
            node = event.Node;
            if isempty(node.NodeData)
                node.Text = event.PreviousText;
                return;
            end
            node.NodeData.Name = node.Text;
            if isequal(app.Tree.SelectedNodes, node)
                showPart(app, node);
            end
        end

        % Value changed function: QuantityField
        function QuantityFieldValueChanged(app, event)
            node = app.Tree.SelectedNodes;
            if ~isempty(node) && ~isempty(node.NodeData)
                node.NodeData.Quantity = app.QuantityField.Value;
            end
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 600 360];
            app.UIFigure.Name = 'Parts';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.RowHeight = {'1x', 22};

            % Create Tree
            app.Tree = uitree(app.GridLayout);
            app.Tree.SelectionChangedFcn = createCallbackFcn(app, @TreeSelectionChanged, true);
            app.Tree.NodeTextChangedFcn = createCallbackFcn(app, @TreeNodeTextChanged, true);
            app.Tree.Editable = 'on';
            app.Tree.Layout.Row = 1;
            app.Tree.Layout.Column = 1;

            % Create DetailsPanel
            app.DetailsPanel = uipanel(app.GridLayout);
            app.DetailsPanel.Title = 'Part';
            app.DetailsPanel.Layout.Row = 1;
            app.DetailsPanel.Layout.Column = 2;

            % Create DetailsGrid
            app.DetailsGrid = uigridlayout(app.DetailsPanel);
            app.DetailsGrid.ColumnWidth = {80, '1x'};
            app.DetailsGrid.RowHeight = {22, 22, 22, '1x'};

            % Create NameLabel
            app.NameLabel = uilabel(app.DetailsGrid);
            app.NameLabel.HorizontalAlignment = 'right';
            app.NameLabel.Layout.Row = 1;
            app.NameLabel.Layout.Column = 1;
            app.NameLabel.Text = 'Name';

            % Create NameValue
            app.NameValue = uilabel(app.DetailsGrid);
            app.NameValue.Layout.Row = 1;
            app.NameValue.Layout.Column = 2;
            app.NameValue.Text = '';

            % Create CategoryLabel
            app.CategoryLabel = uilabel(app.DetailsGrid);
            app.CategoryLabel.HorizontalAlignment = 'right';
            app.CategoryLabel.Layout.Row = 2;
            app.CategoryLabel.Layout.Column = 1;
            app.CategoryLabel.Text = 'Category';

            % Create CategoryValue
            app.CategoryValue = uilabel(app.DetailsGrid);
            app.CategoryValue.Layout.Row = 2;
            app.CategoryValue.Layout.Column = 2;
            app.CategoryValue.Text = '';

            % Create QuantityLabel
            app.QuantityLabel = uilabel(app.DetailsGrid);
            app.QuantityLabel.HorizontalAlignment = 'right';
            app.QuantityLabel.Layout.Row = 3;
            app.QuantityLabel.Layout.Column = 1;
            app.QuantityLabel.Text = 'Quantity';

            % Create QuantityField
            app.QuantityField = uieditfield(app.DetailsGrid, 'numeric');
            app.QuantityField.Limits = [0 Inf];
            app.QuantityField.RoundFractionalValues = 'on';
            app.QuantityField.ValueChangedFcn = createCallbackFcn(app, @QuantityFieldValueChanged, true);
            app.QuantityField.Layout.Row = 3;
            app.QuantityField.Layout.Column = 2;

            % Create HintLabel
            app.HintLabel = uilabel(app.GridLayout);
            app.HintLabel.Layout.Row = 2;
            app.HintLabel.Layout.Column = [1 2];
            app.HintLabel.Text = 'Double-click a part to rename it; its quantity can be edited on the right.';

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = PartsTreeApp

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
