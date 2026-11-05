classdef comp2 < matlab.ui.componentcontainer.ComponentContainer

    % Properties that correspond to underlying components
    properties (Access = private, Transient, NonCopyable)
        GridLayout  matlab.ui.container.GridLayout
    end

    methods (Access = protected)
        
        % Code that executes when the value of a property is changed
        function update(comp)
            % Use this function to update the underlying components
            
        end

        % Create the underlying components
        function setup(comp)

            comp.Position = [1 1 320 240];

            % Create GridLayout
            comp.GridLayout = uigridlayout(comp);
        end
    end
end
