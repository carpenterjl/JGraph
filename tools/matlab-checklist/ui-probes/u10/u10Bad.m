classdef u10Bad < matlab.ui.componentcontainer.ComponentContainer
    % U10 probe: setup or update fails, as asked by the Mode property's default in a subclass.
    properties
        Mode = 'none'
    end
    methods (Access = protected)
        function setup(comp)
            u10log('bad setup');
            if strcmp(getappdata(groot, 'u10bad'), 'setup')
                error('u10:setup', 'setup failed');
            end
        end
        function update(comp)
            u10log('bad update');
            if strcmp(getappdata(groot, 'u10bad'), 'update')
                error('u10:update', 'update failed');
            end
        end
    end
end
