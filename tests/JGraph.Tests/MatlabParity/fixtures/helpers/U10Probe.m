classdef U10Probe < matlab.ui.componentcontainer.ComponentContainer
    % U10 fixtures: a custom component that logs its setup and update, with two callback events
    % and a plain one.
    properties
        Value (1,1) double = 1
        Label char = 'lbl'
    end
    properties (Access = private)
        Grid
        Field
        Hidden0 = 0
    end
    properties (SetAccess = private)
        SetupCalls = 0
        UpdateCalls = 0
    end
    events (HasCallbackProperty, NotifyAccess = public)
        ValueChanged
        Clicked
    end
    events
        Plain
    end
    methods (Access = protected)
        function setup(comp)
            comp.SetupCalls = comp.SetupCalls + 1;
            u10_log(sprintf('setup Value=%g Pos=%s Parent=%s', comp.Value, mat2str(comp.Position), comp.Parent.Type));
            comp.Grid = uigridlayout(comp, [1 1], 'Padding', 0);
            comp.Field = uieditfield(comp.Grid, 'numeric');
        end
        function update(comp)
            comp.UpdateCalls = comp.UpdateCalls + 1;
            u10_log(sprintf('update Value=%g', comp.Value));
            comp.Field.Value = comp.Value;
        end
    end
    methods
        function fire(comp, name)
            notify(comp, name);
        end
        function poke(comp)
            comp.Hidden0 = comp.Hidden0 + 1;
        end
        function g = grid(comp)
            g = comp.Grid;
        end
        function v = fieldValue(comp)
            v = comp.Field.Value;
        end
        function delete(~)
            u10_log('user delete');
        end
    end
end
