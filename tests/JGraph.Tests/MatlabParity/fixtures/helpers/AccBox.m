classdef AccBox
    % A value class with logging accessors on p, a Dependent q read and written through p, a
    % struct property s with accessors, and a validated n with a set method (property_accessors.m).
    properties
        p = [1 2 3]
        s = struct('f', [1 2 3])
        n (1,1) double {mustBePositive} = 1
    end
    properties (Dependent)
        q
    end
    methods
        function obj = AccBox(v)
            if nargin > 0
                obj.p = v;
            end
        end
        function v = get.p(obj)
            vlog('get');
            v = obj.p;
        end
        function obj = set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
        function v = get.q(obj)
            vlog('getq');
            v = obj.p * 2;
        end
        function obj = set.q(obj, v)
            vlog('setq');
            obj.p = v / 2;
        end
        function v = get.s(obj)
            vlog('gets');
            v = obj.s;
        end
        function obj = set.s(obj, v)
            vlog('sets');
            obj.s = v;
        end
        function obj = set.n(obj, v)
            vlog('setn');
            obj.n = v;
        end
        function v = peek(obj)
            v = obj.p;
        end
    end
end
