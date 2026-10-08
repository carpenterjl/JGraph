classdef U10Late < matlab.ui.componentcontainer.ComponentContainer
    % U10 fixtures: a component that tries to make a label in update rather than in setup.
    properties (Access = private)
        Lbl = []
    end
    methods (Access = protected)
        function setup(~)
        end
        function update(comp)
            if isempty(comp.Lbl)
                try
                    comp.Lbl = uilabel(comp);
                    u10_log('label made in update');
                catch e
                    u10_log(['update refused: ' e.identifier ' ' e.message]);
                end
            end
        end
    end
end
