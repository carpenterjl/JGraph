classdef CtorLog < handle
    % A handle class whose constructor logs to vlog, so a fixture can see that load runs none.
    properties
        n = 0
    end
    methods
        function obj = CtorLog()
            vlog('ctor');
        end
    end
end
