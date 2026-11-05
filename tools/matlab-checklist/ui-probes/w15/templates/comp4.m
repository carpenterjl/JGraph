classdef comp4 < matlab.ui.componentcontainer.ComponentContainer

    % Properties that correspond to underlying components
    properties (GetAccess = ?matlab.unittest.TestCase, SetAccess = private, Transient, NonCopyable)
        Label  matlab.ui.control.Label
    end

    properties (AbortSet, Access = public, GetObservable, Hidden, SetObservable)
        Title char = 'Hello'; % Shown in the label
    end
    
    methods (Access = protected)
        
        % Code that executes when the value of a property is changed
        function update(comp)
            comp.Label.Text = comp.Title;
        end

        % Create the underlying components
        function setup(comp)

            comp.Position = [1 1 200 60];
            comp.BackgroundColor = [1 1 0.8];
            comp.Visible = 'off';

            % Create Label
            comp.Label = uilabel(comp);
            comp.Label.Position = [10 10 180 40];
        end
    end
end
