classdef u10Late < matlab.ui.componentcontainer.ComponentContainer
    % U10 probe: a component made in update rather than in setup.
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
                    u10log('label made in update');
                catch e
                    u10log(['update refused: ' e.identifier ' ' e.message]);
                end
            end
        end
    end
end
