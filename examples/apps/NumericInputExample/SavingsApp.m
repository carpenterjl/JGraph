classdef SavingsApp < matlab.apps.AppBase

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure         matlab.ui.Figure
        GridLayout       matlab.ui.container.GridLayout
        DepositLabel     matlab.ui.control.Label
        DepositField     matlab.ui.control.NumericEditField
        RateLabel        matlab.ui.control.Label
        RateField        matlab.ui.control.NumericEditField
        YearsLabel       matlab.ui.control.Label
        YearsField       matlab.ui.control.NumericEditField
        CalculateButton  matlab.ui.control.Button
        BalanceLabel     matlab.ui.control.Label
        UIAxes           matlab.ui.control.UIAxes
    end


    methods (Access = private)

        % Works out the balance month by month, a deposit made at the
        % start of each month and the interest added at its end, plots it
        % and says what it comes to
        function calculate(app)
            months = 12 * app.YearsField.Value;
            rate = app.RateField.Value / 100 / 12;
            balance = zeros(1, months);
            total = 0;
            for k = 1:months
                total = (total + app.DepositField.Value) * (1 + rate);
                balance(k) = total;
            end
            plot(app.UIAxes, (1:months) / 12, balance, 'LineWidth', 1.5);
            app.BalanceLabel.Text = sprintf('Balance after %d years: %.2f', app.YearsField.Value, total);
        end
    end


    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function startupFcn(app)
            calculate(app);
        end

        % Button pushed function: CalculateButton
        function CalculateButtonPushed(app, event)
            calculate(app);
        end

        % Value changed function: DepositField, RateField, YearsField
        function InputValueChanged(app, event)
            calculate(app);
        end
    end

    % Component initialization
    methods (Access = private)

        % Create UIFigure and components
        function createComponents(app)

            % Create UIFigure and hide until all components are created
            app.UIFigure = uifigure('Visible', 'off');
            app.UIFigure.Position = [100 100 640 480];
            app.UIFigure.Name = 'Savings Calculator';

            % Create GridLayout
            app.GridLayout = uigridlayout(app.UIFigure);
            app.GridLayout.ColumnWidth = {150, '1x'};
            app.GridLayout.RowHeight = {22, 22, 22, 22, 22, '1x'};

            % Create DepositLabel
            app.DepositLabel = uilabel(app.GridLayout);
            app.DepositLabel.HorizontalAlignment = 'right';
            app.DepositLabel.Layout.Row = 1;
            app.DepositLabel.Layout.Column = 1;
            app.DepositLabel.Text = 'Monthly deposit';

            % Create DepositField
            app.DepositField = uieditfield(app.GridLayout, 'numeric');
            app.DepositField.Limits = [0 Inf];
            app.DepositField.ValueDisplayFormat = '%.2f';
            app.DepositField.ValueChangedFcn = createCallbackFcn(app, @InputValueChanged, true);
            app.DepositField.Layout.Row = 1;
            app.DepositField.Layout.Column = 2;
            app.DepositField.Value = 100;

            % Create RateLabel
            app.RateLabel = uilabel(app.GridLayout);
            app.RateLabel.HorizontalAlignment = 'right';
            app.RateLabel.Layout.Row = 2;
            app.RateLabel.Layout.Column = 1;
            app.RateLabel.Text = 'Interest rate (%)';

            % Create RateField
            app.RateField = uieditfield(app.GridLayout, 'numeric');
            app.RateField.Limits = [0 100];
            app.RateField.ValueChangedFcn = createCallbackFcn(app, @InputValueChanged, true);
            app.RateField.Layout.Row = 2;
            app.RateField.Layout.Column = 2;
            app.RateField.Value = 5;

            % Create YearsLabel
            app.YearsLabel = uilabel(app.GridLayout);
            app.YearsLabel.HorizontalAlignment = 'right';
            app.YearsLabel.Layout.Row = 3;
            app.YearsLabel.Layout.Column = 1;
            app.YearsLabel.Text = 'Years';

            % Create YearsField
            app.YearsField = uieditfield(app.GridLayout, 'numeric');
            app.YearsField.Limits = [1 60];
            app.YearsField.RoundFractionalValues = 'on';
            app.YearsField.ValueChangedFcn = createCallbackFcn(app, @InputValueChanged, true);
            app.YearsField.Layout.Row = 3;
            app.YearsField.Layout.Column = 2;
            app.YearsField.Value = 10;

            % Create CalculateButton
            app.CalculateButton = uibutton(app.GridLayout, 'push');
            app.CalculateButton.ButtonPushedFcn = createCallbackFcn(app, @CalculateButtonPushed, true);
            app.CalculateButton.Layout.Row = 4;
            app.CalculateButton.Layout.Column = 2;
            app.CalculateButton.Text = 'Calculate';

            % Create BalanceLabel
            app.BalanceLabel = uilabel(app.GridLayout);
            app.BalanceLabel.Layout.Row = 5;
            app.BalanceLabel.Layout.Column = [1 2];
            app.BalanceLabel.Text = '';

            % Create UIAxes
            app.UIAxes = uiaxes(app.GridLayout);
            title(app.UIAxes, 'Balance')
            xlabel(app.UIAxes, 'Years')
            ylabel(app.UIAxes, 'Balance')
            app.UIAxes.XGrid = 'on';
            app.UIAxes.YGrid = 'on';
            app.UIAxes.Layout.Row = 6;
            app.UIAxes.Layout.Column = [1 2];

            % Show the figure after all components are created
            app.UIFigure.Visible = 'on';
        end
    end

    % App creation and deletion
    methods (Access = public)

        % Construct app
        function app = SavingsApp

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
