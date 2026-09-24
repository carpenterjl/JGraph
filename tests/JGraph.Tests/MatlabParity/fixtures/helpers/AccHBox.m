classdef AccHBox < handle
    % The handle form of AccBox: logging accessors on p (the getter counts its calls in hits), a
    % Dependent q read and written through p (property_accessors.m).
    properties
        p = [1 2 3]
        hits = 0
    end
    properties (Dependent)
        q
    end
    methods
        function v = get.p(obj)
            vlog('get');
            obj.hits = obj.hits + 1;
            v = obj.p;
        end
        function set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
        function v = get.q(obj)
            vlog('getq');
            v = obj.p * 2;
        end
        function set.q(obj, v)
            vlog('setq');
            obj.p = v / 2;
        end
    end
end
