classdef DeleteLogger < handle
    % A handle class whose destructor logs its tag through vlog, so a fixture can record when it ran.
    properties
        tag = ''
    end
    methods
        function obj = DeleteLogger(t)
            obj.tag = t;
        end
        function delete(obj)
            vlog(obj.tag);
        end
    end
end
