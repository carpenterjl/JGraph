classdef u10Ctor < matlab.ui.componentcontainer.ComponentContainer
    % U10 probe: a component with its own constructor, and one that writes a property in update.
    properties
        Count = 0
        Extra = ''
    end
    methods
        function comp = u10Ctor(extra, varargin)
            u10log('ctor before super');
            comp@matlab.ui.componentcontainer.ComponentContainer(varargin{:});
            u10log(sprintf('ctor after super Count=%g', comp.Count));
            comp.Extra = extra;
        end
    end
    methods (Access = protected)
        function setup(comp)
            u10log(sprintf('ctor setup Extra=%s', comp.Extra));
        end
        function update(comp)
            u10log(sprintf('ctor update Count=%g Extra=%s', comp.Count, comp.Extra));
            if comp.Count < 3
                comp.Count = comp.Count + 1;   % a write inside update: another update?
            end
        end
    end
end
