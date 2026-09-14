classdef EventBox < handle
    % A handle class with one event. JGraph's parser refuses an events block (appendix A #106), so
    % only value_isolation_events.m constructs it.
    events
        Changed
    end
    methods
        function fire(obj)
            notify(obj, 'Changed');
        end
    end
end
