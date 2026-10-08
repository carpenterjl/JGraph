classdef U10Bad < matlab.ui.componentcontainer.ComponentContainer
    % U10 fixtures: setup or update fails when u10_mode says so.
    properties
        Mode = 'none'
    end
    methods (Access = protected)
        function setup(~)
            u10_log('bad setup');
            if strcmp(u10_mode(), 'setup')
                error('u10:setup', 'setup failed');
            end
        end
        function update(~)
            u10_log('bad update');
            if strcmp(u10_mode(), 'update')
                error('u10:update', 'update failed');
            end
        end
    end
end
