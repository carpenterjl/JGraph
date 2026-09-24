classdef AccGetWrites < handle
    % get.p writes obj.p back, which calls set.p: only the read is the storage's inside a getter
    % (property_accessors.m).
    properties
        p = [1 2 3]
    end
    methods
        function v = get.p(obj)
            vlog('get');
            obj.p = obj.p;
            v = obj.p;
        end
        function set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
    end
end
