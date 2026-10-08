classdef U10Ctor < matlab.ui.componentcontainer.ComponentContainer
    % U10 fixtures: a component with its own constructor, which writes a property in update.
    properties
        Count = 0
        Extra = ''
    end
    methods
        function comp = U10Ctor(extra, varargin)
            u10_log('ctor before super');
            comp@matlab.ui.componentcontainer.ComponentContainer(varargin{:});
            u10_log(sprintf('ctor after super Count=%g', comp.Count));
            comp.Extra = extra;
        end
    end
    methods (Access = protected)
        function setup(comp)
            u10_log(sprintf('ctor setup Extra=%s', comp.Extra));
        end
        function update(comp)
            u10_log(sprintf('ctor update Count=%g Extra=%s', comp.Count, comp.Extra));
            if comp.Count < 3
                comp.Count = comp.Count + 1;
            end
        end
    end
end
