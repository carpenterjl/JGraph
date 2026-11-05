classdef comp3 < matlab.ui.componentcontainer.ComponentContainer

    % Properties that correspond to underlying components
    properties (Access = private, Transient, NonCopyable)
        GridLayout  matlab.ui.container.GridLayout
        Button      matlab.ui.control.Button
    end

    % Events with associated public callbacks
    events (HasCallbackProperty, NotifyAccess = private)
        Cleared
        ValueChanged % Fires when the value changes
    end

    properties (Access = private)
        Scale double = 1; % A private scale
    end
    
    properties (Access = public)
        Limits (1,2) double {mustBeNumeric(Limits)} = [0 100]; % The range
        Value (1,1) double {mustBeInRange(Value, 0, 100, "inclusive")} = 0;
    end
    
    properties (Access = private)
        Count % Description
    end

    % Callbacks that handle component events
    methods (Access = private)

        % Code that executes after component creation
        function postSetupFcn(comp)
            comp.Count = 0;
        end

        % Button pushed function: Button
        function ButtonPushed(comp, event)
            comp.Value = comp.Value + 1;
            notify(comp, 'ValueChanged');
        end
    end

    methods (Access = protected)
        
        % Code that executes when the value of a property is changed
        function update(comp)
            comp.Button.Text = num2str(comp.Value);
        end

        % Create the underlying components
        function setup(comp)

            comp.Position = [1 1 320 240];

            % Create GridLayout
            comp.GridLayout = uigridlayout(comp);

            % Create Button
            comp.Button = uibutton(comp.GridLayout, 'push');
            comp.Button.ButtonPushedFcn = matlab.apps.createCallbackFcn(comp, @ButtonPushed, true);
            comp.Button.Layout.Row = 1;
            comp.Button.Layout.Column = 1;
            
            % Execute the postSetup function
            postSetupFcn(comp)
        end
    end
end
