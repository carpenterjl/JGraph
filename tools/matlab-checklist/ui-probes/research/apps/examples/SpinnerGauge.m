classdef SpinnerGauge < matlab.ui.componentcontainer.ComponentContainer
    % SPINNERGAUGE  A custom UI component: a spinner and a linear gauge kept in step.
    %   c = SpinnerGauge(parent, 'Value', 40, 'Limits', [0 100]);
    %   c.ValueChangedFcn = @(src, evt) disp(evt.Value);

    properties
        Value (1,1) double = 0
        Limits (1,2) double = [0 100]
    end

    properties (Access = private, Transient, NonCopyable)
        Grid      matlab.ui.container.GridLayout
        Spinner   matlab.ui.control.Spinner
        Gauge     matlab.ui.control.LinearGauge
    end

    properties (SetAccess = private)
        SetupCalls  = 0
        UpdateCalls = 0
    end

    events (HasCallbackProperty, NotifyAccess = protected)
        ValueChanged
    end

    methods (Access = protected)
        function setup(comp)
            comp.SetupCalls = comp.SetupCalls + 1;
            comp.Position = [10 10 220 80];
            comp.Grid = uigridlayout(comp, [2 1], 'RowHeight', {'fit', '1x'}, 'Padding', 0);
            comp.Spinner = uispinner(comp.Grid, 'ValueChangedFcn', @(src, evt) comp.onSpin(evt));
            comp.Gauge = uigauge(comp.Grid, 'linear');
        end

        function update(comp)
            comp.UpdateCalls = comp.UpdateCalls + 1;
            comp.Spinner.Limits = comp.Limits;
            comp.Gauge.Limits = comp.Limits;
            comp.Spinner.Value = comp.Value;
            comp.Gauge.Value = comp.Value;
        end
    end

    methods (Access = private)
        function onSpin(comp, evt)
            comp.Value = evt.Value;
            notify(comp, 'ValueChanged');   % runs listeners and the generated ValueChangedFcn
        end
    end
end
